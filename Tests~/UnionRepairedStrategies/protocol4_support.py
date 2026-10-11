#!/usr/bin/env python3
"""Portable evidence logging adapter; measured operation and comparison logic are unchanged."""
import hashlib,json,math,os,statistics,subprocess,tempfile,time
from datetime import datetime,timezone
from pathlib import Path
AUDIT=None

def stamp():return datetime.now(timezone.utc).isoformat()
def save_manifest():
    path=AUDIT['out']/'environment.json';temporary=path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(AUDIT['manifest'],indent=2));os.replace(temporary,path)
def digest(paths):return {AUDIT['portable_path'](p):hashlib.sha256(p.read_bytes()).hexdigest()for p in paths}
def verify_iterations():
    if AUDIT is None or 'iteration_file'not in AUDIT:return
    path=AUDIT['iteration_file']
    if not path.is_file()or hashlib.sha256(path.read_bytes()).hexdigest()!=AUDIT['iteration_sha256']:
        raise RuntimeError('Shared iteration map changed; discard run')
def run(command,**kwargs):
    verify_iterations();command=list(map(str,command));kwargs.pop('capture_output',None)
    index=len(AUDIT['manifest']['CommandLogs']);raw=AUDIT['out']/'command-logs';raw.mkdir(exist_ok=True)
    stdout_path=raw/f'{index:04d}.stdout.txt';stderr_path=raw/f'{index:04d}.stderr.txt'
    numerical=len(command)>2 and command[2]in['test','list','cold','bench']
    public_command=AUDIT['portable_command'](command)
    record=dict(Command=public_command,StartedUTC=stamp(),Status='running',ReturnCode=None,CapturedStdoutSHA256=None,CapturedStderrSHA256=None,Stdout=stdout_path.relative_to(AUDIT['out']).as_posix(),Stderr=stderr_path.relative_to(AUDIT['out']).as_posix(),Disclosure='numeric-json'if numerical else'status-only; tool/build/Git diagnostics omitted')
    AUDIT['manifest']['CommandLogs'].append(record);save_manifest()
    print('+ '+' '.join(public_command),flush=True)
    try:
        remaining=AUDIT['deadline']-time.monotonic()
        if remaining<=0:raise TimeoutError('Whole-run budget exhausted before child execution')
        timeout=min(AUDIT['child_timeout'],remaining);record['TimeoutSeconds']=timeout;save_manifest()
        # Preserve regular-file stdout/stderr plumbing. The temporary diagnostic
        # files are outside the publishable output directory and are removed.
        with tempfile.TemporaryDirectory(prefix='union-private-diagnostics-')as directory:
            private_stdout=Path(directory)/'stdout';private_stderr=Path(directory)/'stderr'
            with private_stdout.open('wb')as stdout,private_stderr.open('wb')as stderr:
                try:result=subprocess.run(command,stdout=stdout,stderr=stderr,check=False,timeout=timeout,**kwargs)
                finally:
                    stdout.flush();stderr.flush()
                    record['CapturedStdoutSHA256']=hashlib.sha256(private_stdout.read_bytes()).hexdigest()
                    record['CapturedStderrSHA256']=hashlib.sha256(private_stderr.read_bytes()).hexdigest()
            stdout_bytes=private_stdout.read_bytes();stderr_bytes=private_stderr.read_bytes()
        record.update(ReturnCode=result.returncode,Status='completed'if result.returncode==0 else'failed')
        if numerical and result.returncode==0:
            json.loads(stdout_bytes);AUDIT['assert_public_report'](stdout_bytes.decode())
            stdout_path.write_bytes(stdout_bytes)
        else:stdout_path.write_text(json.dumps({'Output':'diagnostic transcript omitted','ReturnCode':result.returncode})+'\n')
        stderr_path.write_text(json.dumps({'Output':'diagnostic transcript omitted','ReturnCode':result.returncode})+'\n')
        completed=subprocess.CompletedProcess(command,result.returncode,stdout_bytes.decode(errors='replace'),stderr_bytes.decode(errors='replace'))
        completed.stdout_bytes=stdout_bytes
        completed.check_returncode();return completed
    except BaseException as error:
        record.update(Status='failed',ErrorType=type(error).__name__)
        raise
    finally:
        record['FinishedUTC']=stamp();save_manifest();verify_iterations()

def compare(reports, left, right):
    rows = []
    a_reports, b_reports = reports[left], reports[right]
    for original in a_reports[0]['Rows']:
        key = original['Id']
        a = [next(r for r in report['Rows'] if r['Id'] == key) for report in a_reports]
        b = [next(r for r in report['Rows'] if r['Id'] == key) for report in b_reports]
        result = {'Before': left, 'After': right, 'Id': key, 'Scope': 'deep-diagnostic-not-noninferiority' if key.startswith('deep/') else 'core-comparison'}
        if original['Cohort']:
            before = statistics.median(r['RetainedBytes'] for r in a) / original['Cohort']
            after = statistics.median(r['RetainedBytes'] for r in b) / original['Cohort']
            result.update(BeforeRetainedPerObject=before, AfterRetainedPerObject=after,
                          Status='retained-review' if after > before * 1.05 + 16 else 'retained-no-flag')
        else:
            logs = [math.log(x['Ns'] / y['Ns']) for x, y in zip(a, b)]
            mean = statistics.mean(logs)
            width = 3.012 * statistics.stdev(logs) / math.sqrt(len(logs)) if len(logs) >= 14 else float('inf')
            lower = math.exp(mean - width) if math.isfinite(width) else 0
            upper = math.exp(mean + width) if math.isfinite(width) else None
            old_bytes = statistics.median(r['Bytes'] for r in a)
            new_bytes = statistics.median(r['Bytes'] for r in b)
            status = 'inconclusive'
            if upper is not None and upper < 1 / 1.03: status = 'timing-regression'
            elif lower > 1.03: status = 'timing-improvement'
            if any(r['WallMilliseconds'] < 50 for r in a + b): status = 'short-batch-review'
            if new_bytes > old_bytes: status = 'allocation-regression'
            if key.startswith('deep/'):
                result['AllocationIncreased'] = new_bytes > old_bytes
                result['TimingConclusion'] = 'Diagnostic only; never an acceptance or noninferiority result'
                status = 'deep-diagnostic-short-batch' if any(r['WallMilliseconds'] < 50 for r in a + b) else 'deep-diagnostic'
            result.update(BeforeNs=statistics.median(r['Ns'] for r in a), AfterNs=statistics.median(r['Ns'] for r in b),
                          PairedGeometricSpeedup=math.exp(mean), Approx99Lower=lower, Approx99Upper=upper,
                          BeforeBytes=old_bytes, AfterBytes=new_bytes, Status=status)
        rows.append(result)
    return rows
