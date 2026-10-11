#!/usr/bin/env python3
"""Portable fixed-map 12-variant coordinator. Default mode verifies/plans only; measurement keeps cold stage and omits pilots."""
import argparse,hashlib,json,os,platform,re,sys,time
from pathlib import Path
import protocol4_support as support
from verify import HERE,load,sha,verify

ROUNDS=14
EXPECTED_SDK='10.0.401'
EXPECTED_NET='8.0.31'
EXPECTED_MONO='6.8.0.105'
CONSTANTS={'factory-copy':'','literal-left':'CHAIN_LEFT','literal-right':'CHAIN_RIGHT','seed-alias-aware':'CHAIN_SEED','clear-add-both':'CLEAR_ADD','reusable':'REUSABLE_UNION'}

def files_under(paths):return sorted({p for root in paths for p in root.glob('**/*')if p.is_file()})
def row_signature(report):return[(r['Id'],r['Iterations'],r['Cohort'],r['WarmupIterations'])for r in report['Rows']]
def runtime_expected(report,backend):
    return report['Framework']==('Standalone Mono'if backend=='mono'else'.NET')and report['Runtime']==('4.0.30319.42000'if backend=='mono'else EXPECTED_NET)
def clearance(path,manifest_sha,patch_sha):
    data=load(path)
    for key in ['TimingAuthorized','SemanticValidationComplete','StaticAuditComplete']:
        if data.get(key)is not True:raise RuntimeError('Execution clearance missing: '+key)
    if data.get('CoordinatorManifestSHA256')!=manifest_sha or data.get('CandidatePatchSHA256')!=patch_sha:
        raise RuntimeError('Execution clearance is not bound to this coordinator and candidate')
    evidence={}
    for key in ['SemanticSeal','StaticAudit']:
        record=data[key];file=(path.parent/record['File']).resolve()
        if not re.fullmatch('[0-9a-f]{64}',record['SHA256'])or sha(file)!=record['SHA256']:
            raise RuntimeError('Clearance evidence hash mismatch: '+key)
        evidence[key]={'File':str(file),'SHA256':record['SHA256']}
    return data,evidence

def main():
    start=time.monotonic()
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend',choices=['net','mono'],required=True)
    parser.add_argument('--mode',choices=['plan','test','measure'],default='plan')
    parser.add_argument('--dotnet',type=Path,default=Path('/usr/share/dotnet/dotnet'))
    parser.add_argument('--mono',type=Path,default=Path('/usr/bin/mono'))
    parser.add_argument('--out',type=Path)
    parser.add_argument('--clearance',type=Path)
    parser.add_argument('--git-repository',type=Path)
    parser.add_argument('--snapshot-commit',default=os.environ.get('GITHUB_SHA'))
    parser.add_argument('--snapshot-package-path')
    args=parser.parse_args()
    package=verify();variants=load(HERE/'variants.json');identities=load(HERE/'source-identities.json');provenance=load(HERE/'provenance.json')
    labels=[v['label']for v in variants]
    if len(labels)!=12 or len(set(labels))!=12 or labels[-1]!='repaired-output':raise RuntimeError('Variant/order drift')
    if [v['strategy']for v in variants[:5]]!=['literal-left','literal-right','seed-alias-aware','clear-add-both','factory-copy']:raise RuntimeError('Original strategy order drift')
    map_path=HERE/'maps'/f'{args.backend}.tsv';fixed=dict((key,int(value))for key,value in(line.split('\t')for line in map_path.read_text().splitlines()))
    plan={'Protocol':'repaired-output-public-v1','Mode':args.mode,'Backend':args.backend,'CandidateIdentity':identities['repaired'],'CoordinatorManifestSHA256':sha(HERE/'MANIFEST.json'),'MapSHA256':sha(map_path),'VariantOrder':labels,'RoundOrders':[labels if i%2==0 else list(reversed(labels))for i in range(ROUNDS)],'Rounds':ROUNDS,'TimedRows':45,'RetainedRows':4,'ColdCalls':1476 if args.mode=='measure'else 0,'ColdReplicates':3,'ColdFixtures':41,'CalibrationPilots':0,'WholeRunSeconds':10800,'PerChildSeconds':300,'ExpectedSDK':EXPECTED_SDK,'ExpectedRuntime':EXPECTED_MONO if args.backend=='mono'else EXPECTED_NET,'ExecutionGate':'Matching semantic/static/timing clearance required before all build/test/measurement commands'}
    if args.mode=='plan':print(json.dumps(plan,indent=2));return
    if args.out is None or args.clearance is None or args.git_repository is None:parser.error('Executed modes require --out, --clearance and --git-repository')
    if not args.snapshot_commit or not re.fullmatch('[0-9a-fA-F]{40}',args.snapshot_commit):parser.error('Provide exact --snapshot-commit or GITHUB_SHA')
    repo=args.git_repository.resolve()
    try:package_path=args.snapshot_package_path or HERE.relative_to(repo).as_posix()
    except ValueError:parser.error('Package outside Git checkout requires --snapshot-package-path')
    if not package_path or package_path.startswith('/')or '..'in Path(package_path).parts:parser.error('Use a safe Git-relative package path')
    approval,evidence=clearance(args.clearance.resolve(),plan['CoordinatorManifestSHA256'],provenance['CandidatePatchSHA256'])
    out=args.out.resolve()
    if out.exists()and any(out.iterdir()):parser.error('Output must be new/empty')
    out.mkdir(parents=True,exist_ok=True)
    iteration_file=out/'iterations.tsv';iteration_file.write_bytes(map_path.read_bytes())
    public_approval={key:approval[key]for key in ['TimingAuthorized','SemanticValidationComplete','StaticAuditComplete','CoordinatorManifestSHA256','CandidatePatchSHA256']}
    public_approval['EvidenceSHA256']={key:record['SHA256']for key,record in evidence.items()}
    (out/'execution-clearance.json').write_text(json.dumps(public_approval,indent=2)+'\n')
    manifest=dict(plan,Status='prepared',StartedUTC=support.stamp(),CommandLogs=[],SourceIdentities=identities,SourceAndHarnessHashes={name:sha(HERE/name)for name in package['Files']},SourcePackageManifestSHA256=sha(HERE/'MANIFEST.json'),Clearance=public_approval,ClearanceEvidence={key:{'SHA256':record['SHA256']}for key,record in evidence.items()},ClearanceFileSHA256=sha(args.clearance),ExecutionOrder=[],ProcessHashChecks=[],SharedIterationMapSHA256=sha(iteration_file),SharedIterationMapVerification='Before and after every child; final exit',BinaryHashes={},Machine=platform.machine(),Python=platform.python_version(),PackageDirectory=package_path,Settings={'TieredCompilation':False,'ServerGC':False},OperationContext='Program.cs and Comparator.csproj are byte-identical to protocol4; operands and selected delegate unchanged',OrchestrationDifferences=provenance['OrderChange']+' '+provenance['CalibrationChange'],LoggingDisclosure=provenance['LoggingChange'])
    manifest['Affinity']=sorted(os.sched_getaffinity(0))if hasattr(os,'sched_getaffinity')else'unavailable'
    if Path('/proc/cpuinfo').exists():manifest['CPUModel']=next((line.split(':',1)[1].strip()for line in Path('/proc/cpuinfo').read_text().splitlines()if line.startswith('model name')),'unavailable')
    manifest['LogicalCPUCount']=os.cpu_count()
    def portable_path(path):
        path=Path(path).resolve()
        for root,prefix in [(out,'OUTPUT'),(HERE,package_path),(repo,'.')]:
            try:return prefix+'/'+path.relative_to(root).as_posix()
            except ValueError:pass
        raise RuntimeError('Unmapped artifact path')
    def portable_command(command):
        roots=[(str(out),'OUTPUT'),(str(HERE),package_path),(str(repo),'.'),(str(args.dotnet.resolve()),'$DOTNET'),(str(args.mono.resolve()),'$MONO')]
        result=[]
        for value in command:
            for raw,portable in sorted(roots,key=lambda item:-len(item[0])):value=value.replace(raw,portable)
            result.append(value)
        return result
    def assert_public_report(content):
        # Numeric fixture reports have no absolute filenames or host identifiers.
        if re.search(r'(?<![A-Za-z0-9_])/(?:[^\s/\"]+/)+|[A-Za-z]:\\',content):
            raise RuntimeError('Unexpected absolute path in numerical report')
    support.AUDIT={'portable_path':portable_path,'portable_command':portable_command,'assert_public_report':assert_public_report,'out':out,'manifest':manifest,'started':start,'deadline':start+10800,'child_timeout':300,'iteration_file':iteration_file,'iteration_sha256':sha(iteration_file)}
    support.save_manifest();(out/'preliminary-environment.json').write_text(json.dumps(manifest,indent=2))
    env=os.environ.copy();env.update(DOTNET_TieredCompilation='0',DOTNET_gcServer='0',COMPlus_TieredCompilation='0',COMPlus_gcServer='0')
    host=args.mono.resolve()if args.backend=='mono'else args.dotnet.resolve()
    targets=[];binaries={};assemblies={};failure=None
    manifest['RawReportHashes']={}
    def write_report(path,content):
        assert_public_report(content);path.write_text(content)
        manifest['RawReportHashes'][str(path.relative_to(out))]=sha(path)
    def verify_live():
        if sha(HERE/'MANIFEST.json')!=manifest['SourcePackageManifestSHA256']:raise RuntimeError('Package manifest changed')
        verify();support.verify_iterations()
        actual=support.digest(files_under(targets))
        if actual!=binaries:raise RuntimeError('Completed binary tree changed')
        if sha(args.clearance)!=manifest['ClearanceFileSHA256']:raise RuntimeError('Clearance file changed')
        for record in evidence.values():
            if sha(Path(record['File']))!=record['SHA256']:raise RuntimeError('Clearance evidence changed')
        manifest['ProcessHashChecks'].append({'NextCommandIndex':len(manifest['CommandLogs']),'PackageManifestSHA256':sha(HERE/'MANIFEST.json'),'IterationMapSHA256':sha(iteration_file),'CompletedBinaryFileCount':len(actual)})
    def run(command,input_bytes=None):
        verify_live()
        if input_bytes is not None:manifest.setdefault('CommandInputHashes',{})[str(len(manifest['CommandLogs']))]=hashlib.sha256(input_bytes).hexdigest()
        command_index=len(manifest['CommandLogs'])
        try:return support.run(command,env=env,cwd=HERE,input=input_bytes)
        finally:
            if len(manifest['CommandLogs'])>command_index:
                record=manifest['CommandLogs'][command_index]
                for stream in ['Stdout','Stderr']:
                    path=out/record[stream];record[stream+'SHA256']=sha(path)if path.is_file()else None
            verify_live()
    def git(command,input_bytes=None):
        return run(['git','-C',repo]+command,input_bytes=input_bytes).stdout_bytes
    try:
        # Verify exact C# file sets and raw Git blob bytes, independent of the
        # working tree. Snapshot commit is not represented as root Runtime code.
        resolved=git(['rev-parse',args.snapshot_commit+'^{commit}']).decode().strip()
        if resolved.lower()!=args.snapshot_commit.lower():raise RuntimeError('Snapshot commit resolution mismatch')
        git_proof={'SnapshotCommit':resolved,'PackagePath':package_path,'Snapshots':{}}
        for source_id,identity in identities.items():
            commit=identity['GitRevision']or resolved
            prefix='Runtime'if identity['GitRevision']else package_path+'/sources/repaired/Runtime'
            listing=git(['ls-tree','-r','-z','--full-tree',commit,'--',prefix])
            entries={}
            for item in listing.split(b'\0'):
                if not item:continue
                header,path=item.split(b'\t',1);mode,kind,oid=header.decode().split();path=path.decode()
                if not path.endswith('.cs'):continue
                if kind!='blob'or mode not in ['100644','100755']or not path.startswith(prefix+'/'):raise RuntimeError('Unexpected Git source object')
                name='Runtime'+path[len(prefix):];entries[name]={'Blob':oid,'GitPath':path}
            if set(entries)!=set(identity['RuntimeFileHashes']):raise RuntimeError('Git C# file-set mismatch: '+source_id)
            object_ids=list(dict.fromkeys(item['Blob']for item in entries.values()))
            payload=git(['cat-file','--batch'],input_bytes=('\n'.join(object_ids)+'\n').encode())
            cursor=0;blob_hashes={}
            for oid in object_ids:
                end=payload.index(b'\n',cursor);header=payload[cursor:end].decode().split()
                if len(header)!=3 or header[0]!=oid or header[1]!='blob':raise RuntimeError('Git blob response mismatch')
                size=int(header[2]);cursor=end+1;content=payload[cursor:cursor+size];cursor+=size
                if len(content)!=size or payload[cursor:cursor+1]!=b'\n':raise RuntimeError('Truncated Git blob response')
                cursor+=1;blob_hashes[oid]=hashlib.sha256(content).hexdigest()
            if cursor!=len(payload):raise RuntimeError('Unexpected Git blob response tail')
            for name,item in entries.items():
                actual=blob_hashes[item['Blob']]
                if actual!=identity['RuntimeFileHashes'][name]or actual!=sha(HERE/'sources'/source_id/name):raise RuntimeError('Git/source byte mismatch: '+source_id+'/'+name)
            git_proof['Snapshots'][source_id]={'Commit':commit,'GitPrefix':prefix,'CSharpFileCount':len(entries),'Files':entries,'RuntimeTreeSHA256':identity['RuntimeTreeSHA256']}
        git_proof['RootRuntimeGitTree']=git(['rev-parse',resolved+':Runtime']).decode().strip()
        git_proof['RootRuntimeUse']='Provenance only; not compiled. All builds select package snapshots explicitly.'
        manifest['GitSourceVerification']=git_proof;support.save_manifest()
        run([args.dotnet.resolve(),'--info'])
        sdk_version=run([args.dotnet.resolve(),'--version']).stdout.strip()
        run([args.dotnet.resolve(),'--list-runtimes'])
        if sdk_version!=EXPECTED_SDK:raise RuntimeError('Build SDK drift; requires a separately reviewed protocol')
        manifest['SDKVersion']=EXPECTED_SDK
        if args.backend=='mono':
            mono_version=run([host,'--version']).stdout
            if not re.search(r'\b6\.8\.0\.105\b',mono_version):raise RuntimeError('Mono runtime drift; requires separate review')
            manifest['MonoVersion']=EXPECTED_MONO
        manifest['Status']='building';support.save_manifest()
        for variant in variants:
            label=variant['label'];target=out/label/'bin';obj=out/label/'obj'
            command=[args.dotnet.resolve(),'build',HERE/'Comparator.csproj','-c','Release','-p:UseSharedCompilation=false','-p:MonoTarget='+('true'if args.backend=='mono'else'false'),'-p:RuntimeRoot='+str(HERE/'sources'/variant['source_id']/'Runtime'),'-p:BaseIntermediateOutputPath='+str(obj)+'/', '-p:MSBuildProjectExtensionsPath='+str(obj)+'/','-o',target]
            if CONSTANTS[variant['strategy']]:command+=['-p:DefineConstants='+CONSTANTS[variant['strategy']]]
            run(command);(out/(label+'-build.json')).write_text(json.dumps({'Status':'completed','Variant':label,'Diagnostics':'Omitted; source and portable build command retained'}))
            targets.append(target);binaries.update(support.digest(files_under([target])));manifest['BinaryHashes']=dict(binaries)
            assemblies[label]=target/('Comparator.exe'if args.backend=='mono'else'Comparator.dll');support.save_manifest()
        manifest['Status']='checking';support.save_manifest()
        expected_fixtures=load(HERE/'expected-fixtures.json');fixture_reports={}
        for variant in variants:
            label=variant['label'];result=run([host,assemblies[label],'test']);write_report(out/(label+'-tests.json'),result.stdout);report=json.loads(result.stdout)
            if report.get('Status')!='PASS'or report.get('Strategy')!=variant['strategy']or report.get('FixtureCount')!=43 or report.get('Assertions')!=575015 or not runtime_expected(report,args.backend):raise RuntimeError('Correctness/strategy/runtime mismatch: '+label)
            result=run([host,assemblies[label],'list']);fixtures=json.loads(result.stdout);fixture_reports[label]=fixtures
            expected=expected_fixtures['f53-output'if label=='repaired-output'else label]
            if fixtures!=expected:raise RuntimeError('Fixture/domain/route drift: '+label)
        write_report(out/'fixtures-and-routes.json',json.dumps(fixture_reports,indent=2))
        if args.mode=='measure':
            manifest['Status']='checking-cold';support.save_manifest();cold=out/'cold';cold.mkdir();cold_reports={}
            fixtures=fixture_reports[labels[0]]
            for replicate in range(3):
                order=labels if replicate%2==0 else list(reversed(labels))
                for fixture in fixtures:
                    if not fixture['ColdEligible']:continue
                    for label in order:
                        manifest['ExecutionOrder'].append(['cold',replicate,label,fixture['Id']])
                        result=run([host,assemblies[label],'cold',fixture['Id']]);report=json.loads(result.stdout)
                        safe=fixture['Id'].replace('/','__');write_report(cold/f'{label}-{replicate}-{safe}.json',result.stdout)
                        variant=next(v for v in variants if v['label']==label)
                        route=next(f['Route']for f in fixture_reports[label]if f['Id']==fixture['Id'])
                        if report['Id']!=fixture['Id']or report['Strategy']!=variant['strategy']or report['Route']!=route or not runtime_expected(report,args.backend):raise RuntimeError('Cold identity/route/runtime mismatch')
                        cold_reports[(replicate,fixture['Id'],label)]=report
            cold_comparison=[]
            for replicate in range(3):
                for fixture in fixtures:
                    if not fixture['ColdEligible']:continue
                    key=fixture['Id'];candidate=cold_reports[(replicate,key,'repaired-output')];historical=cold_reports[(replicate,key,'f53-output')]
                    originals=[cold_reports[(replicate,key,label)]for label in labels[:5]]
                    row={'Id':key,'Replicate':replicate,'BestOriginalCold':min(r['FirstCallAllocatedBytes']for r in originals),'BestOriginalWarm':min(r['WarmOperationOnlyAllocatedBytes']for r in originals),'CandidateCold':candidate['FirstCallAllocatedBytes'],'CandidateWarm':candidate['WarmOperationOnlyAllocatedBytes'],'FirstOwnedEqualToF53':candidate['FirstOwned']==historical['FirstOwned'],'WarmOwnedEqualToF53':candidate['WarmOwned']==historical['WarmOwned']}
                    row['AllRouteAllocatedBytes']={label:{'Cold':cold_reports[(replicate,key,label)]['FirstCallAllocatedBytes'],'Warm':cold_reports[(replicate,key,label)]['WarmOperationOnlyAllocatedBytes']}for label in labels}
                    historical_chains=[cold_reports[(replicate,key,label)]for label in labels[5:10]]
                    row.update(BestF53ChainCold=min(r['FirstCallAllocatedBytes']for r in historical_chains),BestF53ChainWarm=min(r['WarmOperationOnlyAllocatedBytes']for r in historical_chains),F53OutputCold=historical['FirstCallAllocatedBytes'],F53OutputWarm=historical['WarmOperationOnlyAllocatedBytes'])
                    row['CandidateMinusControlBytes']={label:{'Cold':row['CandidateCold']-row['AllRouteAllocatedBytes'][label]['Cold'],'Warm':row['CandidateWarm']-row['AllRouteAllocatedBytes'][label]['Warm']}for label in labels[:-1]}
                    row['AdverseControls']=[label for label,delta in row['CandidateMinusControlBytes'].items()if delta['Cold']>0 or delta['Warm']>0]
                    row['Blocking']=bool(row['AdverseControls'])or not row['FirstOwnedEqualToF53']or not row['WarmOwnedEqualToF53']
                    cold_comparison.append(row)
            write_report(out/'cold-comparison.json',json.dumps(cold_comparison,indent=2))
            manifest['ColdGate']={'Calls':len(cold_reports),'ComparisonRows':len(cold_comparison),'BlockingRows':[r for r in cold_comparison if r['Blocking']],'Status':'failed'if any(r['Blocking']for r in cold_comparison)else'passed','Scope':'41 fixed protocol4 fixtures, all 12 routes, three fresh replicates; two nonrepeatable proxy fixtures excluded as before; separate true-empty/dirty supplement not repeated here','PoolReview':'Protocol4 does not collect pool events. The final local source seal retains 18 .NET pool-only review flags separately; they are not converted into byte/state passes by this coordinator.'}
            support.save_manifest()
            if len(cold_reports)!=1476 or any(r['Blocking']for r in cold_comparison):raise RuntimeError('Substantive cold byte/state gate failed; no warm timing is allowed')
            manifest['Status']='measuring';support.save_manifest();raw=out/'raw';raw.mkdir();reports={label:[]for label in labels}
            expected_rows=load(HERE/'expected-rows.json')
            expected_signature=[(row['Id'],0 if row['Cohort']else fixed[row['Id']],row['Cohort'],row['WarmupIterations'])for row in expected_rows]
            first=None
            for round_index in range(ROUNDS):
                for label in plan['RoundOrders'][round_index]:
                    manifest['ExecutionOrder'].append([round_index,label]);result=run([host,assemblies[label],'bench',iteration_file]);write_report(raw/f'{label}-{round_index}.json',result.stdout);report=json.loads(result.stdout)
                    strategy=next(v['strategy']for v in variants if v['label']==label)
                    if report['Strategy']!=strategy or not runtime_expected(report,args.backend)or row_signature(report)!=expected_signature:raise RuntimeError('Measured traversal/strategy/runtime drift')
                    if first is None:first=report
                    for key in ['Schema','Runtime','Framework','RegistryCount','Checksum']:
                        if report[key]!=first[key]:raise RuntimeError('Measured public workload mismatch: '+key)
                    reports[label].append(report)
            comparison=[row for i,left in enumerate(labels)for right in labels[i+1:]for row in support.compare(reports,left,right)]
            write_report(out/'comparison.json',json.dumps(comparison,indent=2));write_report(out/'repaired-comparison.json',json.dumps([row for row in comparison if row['After']=='repaired-output'],indent=2));write_report(out/'flat1024-focus.json',json.dumps([row for row in comparison if row['Id'].startswith('flat/n1024/')],indent=2))
            manifest['ComparisonStatusCounts']={status:sum(row['Status']==status for row in comparison)for status in sorted({row['Status']for row in comparison})}
        manifest['PerformanceAcceptance']='Not evaluated. Capture completion is not acceptance; preserve all adverse/inconclusive results. Every deep row is diagnostic only.'
    except BaseException as error:
        failure=error;manifest.update(Status='failed',FailureType=type(error).__name__,Failure='A required stage or check failed; inspect status and portable command records.');(out/'failure.txt').write_text(json.dumps({'FailureType':type(error).__name__,'RawDiagnostics':'Omitted'}));raise
    finally:
        violations=[]
        try:verify_live()
        except BaseException as error:violations.append('Final immutable-input verification failed: '+type(error).__name__)
        for key,collect in [
            ('FinalSourceAndHarnessHashes',lambda:{name:sha(HERE/name)for name in package['Files']if(HERE/name).is_file()}),
            ('FinalBinaryHashes',lambda:support.digest(files_under(targets))),
            ('FinalSharedIterationMapSHA256',lambda:sha(iteration_file)if iteration_file.exists()else None)]:
            try:manifest[key]=collect()
            except BaseException as error:violations.append(key+': '+type(error).__name__)
        try:
            final_raw={name:sha(out/name)if(out/name).is_file()else None for name in manifest['RawReportHashes']}
            manifest['FinalRawReportHashes']=final_raw
            if final_raw!=manifest['RawReportHashes']:violations.append('Raw report changed')
            for record in manifest['CommandLogs']:
                for stream in ['Stdout','Stderr']:
                    path=out/record[stream];actual=sha(path)if path.is_file()else None
                    if actual!=record.get(stream+'SHA256'):violations.append('Raw command stream changed: '+record[stream])
        except BaseException as error:violations.append('Raw evidence: '+type(error).__name__)
        manifest['TotalElapsedSeconds']=time.monotonic()-start
        if manifest['TotalElapsedSeconds']>10800:violations.append('Whole-run budget exceeded')
        manifest['ImmutabilityViolations']=violations;manifest['FinishedUTC']=support.stamp()
        if violations:manifest['Status']='failed'
        elif failure is None:manifest['Status']='completed'
        support.save_manifest()
        if violations and failure is None:raise RuntimeError('; '.join(violations))
    print('Saved fixed-map evidence in OUTPUT')
if __name__=='__main__':
    try:main()
    except Exception as error:
        # No traceback/argv from a failed subprocess is disclosed to the CI log.
        print('Coordinator failed: '+type(error).__name__+'. See retained status records.',file=sys.stderr)
        sys.exit(1)
