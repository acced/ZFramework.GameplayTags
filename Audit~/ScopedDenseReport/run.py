#!/usr/bin/env python3
"""Summarize only completed fixed artifacts; never rerun or discard timing samples."""
import csv,io,json,hashlib,os,pathlib,re,subprocess,zipfile
HEAD="2b2c87c156ff8287bde1cbb9b671f68917003c8f"
RUN=36978692633
OUT=pathlib.Path("deliverables")
def api(path):return subprocess.check_output(["gh","api",path])
def gate(rows):
    return dict(cases=len(rows),fast=sum(r["faster5"]=="True" for r in rows),
        slow=sum(r["slower5"]=="True" for r in rows),AAstable=sum(r["AAstable"]=="True" for r in rows),
        fast_and_AA=sum(r["faster5"]==r["AAstable"]=="True" for r in rows))
def table(rows,columns):
    lines=["|"+"|".join(columns)+"|","|"+"|".join("---" for _ in columns)+"|"]
    for row in rows:
        vals=[]
        for key in columns:
            v=row[key];vals.append((f"{v:.3f}" if isinstance(v,float) else str(v)).replace("|","/"))
        lines.append("|"+"|".join(vals)+"|")
    return lines

def main():
    OUT.mkdir(exist_ok=True);repo=os.environ["GITHUB_REPOSITORY"]
    run=json.loads(api(f"/repos/{repo}/actions/runs/{RUN}"))
    assert run["head_sha"]==HEAD and run["conclusion"]=="success"
    artifacts=json.loads(api(f"/repos/{repo}/actions/runs/{RUN}/artifacts?per_page=100"))["artifacts"]
    meta=next(a for a in artifacts if a["name"]==f"scoped-dense-reviewed-{RUN}")
    data=api(f"/repos/{repo}/actions/artifacts/{meta['id']}/zip")
    digest=hashlib.sha256(data).hexdigest();assert meta["digest"]=="sha256:"+digest
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        collect=json.loads(z.read("collection.json"));assert collect["head"]==HEAD
        assert collect["source_and_generated_equal"]
        comparisons=list(csv.DictReader(io.StringIO(z.read("All_Comparisons.csv").decode("utf-8-sig"))))
        allrows=list(csv.DictReader(io.StringIO(z.read("All_Architectures.csv").decode("utf-8-sig"))))
        source=z.read("GameplayTags_ScopedDense_TestedSource.zip")
        groups={};public_enum={};focused=[];negative=[];size={};codegen={};pairfocus=[]
        for arch in ("x64","arm64"):
            own=[r for r in comparisons if r["architecture"]==arch and r["control"]=="control"]
            for variant in ("union","copy","combined"):
                v=[r for r in own if r["candidate"]==variant]
                groups[arch+"/"+variant+"/all_bench"]=gate([r for r in v if r["stage"]=="bench"])
                public_enum[arch+"/"+variant]=gate([r for r in v if r["operation"]=="enumerate" and r["left"]=="Micro"])
                for op in ("fresh_union","union_into"):
                    groups[arch+"/"+variant+"/mixed_micro/"+op]=gate([r for r in v if r["operation"]==op and r["relation"]=="mixed" and r["output"]=="Micro" and int(r["members"])>0])
                for op in ("fresh_copyfrom","reuse_copyfrom"):
                    groups[arch+"/"+variant+"/dense_to_micro/"+op]=gate([r for r in v if r["operation"]==op and r["left"]=="Dense" and r["output"]=="Micro" and int(r["members"])>0])
            negative.extend(r for r in own if r["slower5"]=="True")
            for r in own:
                s=json.loads(r["shape"])
                if r["universe"]!="262144" or r["members"] not in ("8","4096") or r["distribution"] not in ("contiguous","scattered") or s["seed"]!=20261031:continue
                if r["output"]=="Micro" and ((r["operation"] in ("fresh_union","union_into") and r["relation"]=="mixed" and r["candidate"] in ("union","combined")) or (r["operation"] in ("fresh_copyfrom","reuse_copyfrom") and r["left"]=="Dense" and r["candidate"] in ("copy","combined"))):
                    focused.append(r)
            for variant in ("control","union","copy","combined"):
                test=json.loads(z.read(f"{arch}/results/{variant}-tests.json"))
                size[arch+"/"+variant]={k:test[k] for k in ("assertions","scopedAssertions","failures","cursorBytes","enumeratorBytes","privateCursorBytes")}
                text=z.read(f"{arch}/results/{variant}-codegen.log").decode()
                codegen[arch+"/"+variant]=[{"method":m.group(1),"bytes":int(m.group(2))} for m in re.finditer(r"; Assembly listing for method ([^\n]+)\n.*?; Total bytes of code (\d+)",text,re.S)]
        for r in allrows:
            s=json.loads(r["shape"])
            if r["stage"]=="pairs" and r["label"] in ("control","combined") and r["universe"]=="262144" and r["members"] in ("8","4096"):
                pairfocus.append(r)
        result=dict(head=HEAD,run=RUN,artifact_id=meta["id"],archive_sha256=digest,
            counts={arch:{k:v for k,v in d.items() if k in ("aggregate_rows","raw_rows","timing_samples","zero_allocation_samples")} for arch,d in collect["architectures"].items()},
            groups=groups,public_micro_enumeration=public_enum,tests=size,codegen=codegen,
            focus=focused,regressions=negative,pair_rows=pairfocus,
            AA={arch:d["AA"] for arch,d in collect["architectures"].items()},
            all_rows=len(allrows),comparison_rows=len(comparisons),production_promoted=False)
        lines=["# GameplayTags：私有 Dense 物化路径验证","","日期：2026-10-02。PR #16。",
            "",f"实际计时提交：`{HEAD}`。固定对照为 PR15 仅直接 Dense 结果候选，不是被淘汰的共享游标版。",
            "","## 范围与合同",
            "","union：仅优化独立 Dense+Micro→Micro 并集。copy：仅优化 Dense→Micro CopyFrom。combined：两项实际组合。",
            "公开 Records / Enumerator 源码和布局不变；新的私有游标只存在于上述内部调用。不新增集合字段、成员表示、对象池、延迟计数或自动转换。",
            "普通 Clone、Append/Remove 内核、输出别名路径、两个 Dense 输入转 Micro 和公开 Dense foreach 仍为原实现。CopyAsStorage 的自有转换方法未改。",
            "新建 CopyFrom 是真实构造目标再调用 CopyFrom，不能替代原四项中的独立克隆。输出容量、增长、准确 Count、独立所有权和输入验证保持原合同。",
            "","## 完整目标与负例统计",
            "","fast/slow：三轮同时比较同二进制 A/A 后的5%筛选；不是统计置信区间。AAstable：双方每轮 A/A 差异不超过5%。"]
        lines+=table([dict(group=k,**v) for k,v in groups.items()],["group","cases","fast","slow","AAstable","fast_and_AA"])
        lines += ["","## 代表测点","","单位 ns/完整公开操作；两输入方向都保留，新建包含独立分配与准确计数。"]
        shown=[dict(arch=r["architecture"],variant=r["candidate"],n=r["members"],dist=r["distribution"],left=r["left"],right=r["right"],op=r["operation"],control_ns=float(r["baseline_ns"]),new_ns=float(r["candidate_ns"]),bytes=r["bytes"],AA=r["AAstable"]) for r in focused]
        lines+=table(shown,["arch","variant","n","dist","left","right","op","control_ns","new_ns","bytes","AA"])
        lines += ["","## 公共 Micro 枚举回退检查"]
        lines+=table([dict(group=k,**v) for k,v in public_enum.items()],["group","cases","fast","slow","AAstable","fast_and_AA"])
        lines += ["","## 实际类型大小与测试计数","","privateCursorBytes为内部局部值类型大小；0B托管分配不代表没有栈、复制或分支成本。断言次数包含循环，不是等量独立案例。"]
        lines+=table([dict(build=k,**v) for k,v in size.items()],["build","assertions","scopedAssertions","failures","cursorBytes","enumeratorBytes","privateCursorBytes"])
        lines += ["","## 全部持续回退","","未改方法的回退仍保留，不自动视作无效噪声。完整原始样本、三轮中位数、GC和A/A在原证据包及CSV。"]
        shown=[dict(arch=r["architecture"],candidate=r["candidate"],stage=r["stage"],operation=r["operation"],n=r["members"],u=r["universe"],dist=r["distribution"],left=r["left"],right=r["right"],output=r["output"],ratio=float(r["ratio"]),AA=r["AAstable"]) for r in negative]
        lines+=table(shown,["arch","candidate","stage","operation","n","u","dist","left","right","output","ratio","AA"])
        lines += ["","## 证据与限制","",f"原 CI run {RUN}；收集包 SHA256 `{digest}`。",
            "收集阶段重新下载两平台原 ZIP，重算全部中位数、分配、排名与A/A，并检查源码树、生成C#及受保护生产文件。此报告只解析该固定数据，不重测、不删除样本、不调整自动选择阈值。",
            "同平台、同轮对照有效；不能与历史另一台主机的绝对ns拼接。保留所有慢样本和GC计数。真实Unity/Mono/IL2CPP/Burst/手机、冷缓存多角色、长期峰值内存、全部历史main/Alex对照未执行。",
            "没有生产迁移或原四项全面领先声明。源码ZIP不含.git；完整重现依赖指定提交的全历史检出。"]
        report="\n".join(lines)+"\n"
        (OUT/"固定结果.json").write_text(json.dumps(result,ensure_ascii=False,indent=2))
        (OUT/"中文验证报告.md").write_text(report)
        (OUT/"GameplayTags_ScopedDense_TestedSource_2b2c87c.zip").write_bytes(source)
        (OUT/"双平台完整矩阵.csv").write_bytes(z.read("All_Architectures.csv"))
        (OUT/"全部比较与负例.csv").write_bytes(z.read("All_Comparisons.csv"))
        with zipfile.ZipFile(OUT/"GameplayTags_ScopedDense_源码与双平台验证_2b2c87c.zip","w",zipfile.ZIP_DEFLATED) as bundle:
            bundle.writestr("original-reviewed-evidence.zip",data)
            bundle.writestr("GameplayTags_ScopedDense_TestedSource_2b2c87c.zip",source)
            bundle.writestr("中文验证报告.md",report)
            bundle.writestr("固定结果.json",json.dumps(result,ensure_ascii=False,indent=2))
            bundle.writestr("双平台完整矩阵.csv",z.read("All_Architectures.csv"))
            bundle.writestr("全部比较与负例.csv",z.read("All_Comparisons.csv"))
    print("SUMMARY_GATES "+json.dumps(groups))
    print("SUMMARY_PUBLIC_ENUM "+json.dumps(public_enum))
    print("SUMMARY_TESTS "+json.dumps(size))
    print("SUMMARY_CODEGEN "+json.dumps(codegen))
    print("SUMMARY_FOCUS "+json.dumps(focused))
    print("SUMMARY_REGRESSIONS "+json.dumps(negative))
    print("SUMMARY_PAIR_ROWS "+json.dumps(pairfocus))
    print("SUMMARY_IDENTITY "+json.dumps({k:result[k] for k in ("head","run","artifact_id","archive_sha256","counts","all_rows","comparison_rows","AA")}))
if __name__=="__main__":main()
