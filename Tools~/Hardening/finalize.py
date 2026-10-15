"""One-time follow-up patch; runtime sources remain explicit and directly buildable."""
import base64,hashlib,json,pathlib,re,subprocess,zlib
root=pathlib.Path(__file__).resolve().parents[2];here=root/'Tools~/Hardening'
if (here/'finalized.json').exists():raise SystemExit(0)
t=''.join((here/f'final-{i}.txt').read_text().strip() for i in range(3))
# Correct a duplicated transport substring; the compressed checksum is authoritative.
t=t.replace('trixeZIeZIe6','trixeZIe6')
b=base64.b64decode(t,validate=True)
assert hashlib.sha256(b).hexdigest()=='6ee90f2ddc3f1d7f782586225602e50b9c36acae21ce653466e7f1cf35a043be'
data=json.loads(zlib.decompress(b))
allowed={'Tools~/Hardening/extra.patch','Tools~/Hardening/Benchmark.cs','Tools~/Hardening/benchmark.py','Tools~/Hardening/package.py','Documentation~/HARDENING.md'}
assert set(data)==allowed
for name,text in data.items():
 p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding='utf-8',newline='\n')
subprocess.run(['git','apply','--whitespace=nowarn',str(here/'extra.patch')],cwd=root,check=True)
# Canonical-name guarding also makes a field-to-property/schema migration unnecessary.
p=root/'Runtime/GameplayTag.cs';s=p.read_text()
a=s.index('        // Source-compatible assignment invalidates the ID.')
z=s.index('        [NonSerialized]',a)
s=s[:a]+s[z:]
s=s.replace('        [UnityEngine.Serialization.FormerlySerializedAs("m_Name")]\n','')
s=s.replace('private string m_SerializedName;', 'public string m_SerializedName;').replace('m_SerializedName','m_Name')
p.write_text(s)
for name in ['GameplayTagPropertyDrawer.cs','GameplayTagTreeView.cs']:
 p=root/'Editor'/name;p.write_text(p.read_text().replace('"m_SerializedName"','"m_Name"'))
p=root/'Tests~/Hardening/Program.cs';s=p.read_text().replace('GetField("m_SerializedName", System.Reflection.BindingFlags.NonPublic', 'GetField("m_Name", System.Reflection.BindingFlags.Public');p.write_text(s)
p=root/'Documentation~/HARDENING.md';s=p.read_text();s=s.replace('修改 m_Name 会使 ID 失效；直接覆盖序列化背后字段也通过规范名称引用检查避免旧 ID 与新名称错配。','修改公开 m_Name 后，规范名称引用检查会拒绝旧缓存 ID，并转入名称解析。')
s=s.replace('整数 2.0 的 `tag.m_Name` 仍能读写，但现在是属性，不能继续按字段反射或以 ref 传递。Unity 背后字段为 `m_SerializedName`，使用 FormerlySerializedAs("m_Name")；.NET DataContract 名仍为 m_Name。Editor drawer 已同步。','整数 2.0 的公开 `tag.m_Name` 字段保持不变，Unity 字段名和 Editor drawer 不需要迁移；.NET DataContract 仍保存同名字段。名称变化通过查询时的引用一致性检查处理，而不是依赖结构体序列化回调。')
p.write_text(s)
p=root/'package.json';info=json.loads(p.read_text(encoding='utf-8-sig'));info['version']='2.1.0-rc.1';p.write_text(json.dumps(info,ensure_ascii=False,indent=2)+'\n')
(root/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}\n')
p=root/'README.md';s=p.read_text(encoding='utf-8-sig');p.write_text('# Integer hardening candidate — 2.1.0-rc.1\n\n本轮采用上传整数版，新增安全性与序列化修复；并非原 name-based rc.1 的兼容替换。详见 [加固说明与迁移边界](Documentation~/HARDENING.md)。性能收益与检查成本必须同时评估，未执行真实 Unity/IL2CPP 验收。以下为整数路线原有使用说明，性能历史不能解释为本轮实测。\n\n'+s)
(root/'CHANGELOG.md').write_text('# Changelog\n\n## 2.1.0-rc.1\n\nName/ID consistency, deferred serialization binding, invalid-input/count/parent-transfer guards, generation-checked pool leases, optional checked enumeration, and dense/sparse Clear paths. Original public m_Name field is retained. See Documentation~/HARDENING.md for API boundaries, costs, tests and migration. Not a universal speed or Unity certification claim.\n')
for i in range(3):(here/f'final-{i}.txt').unlink()
(here/'finalized.json').write_text(json.dumps({'uploaded_baseline':'Tests~/Hardening/Baseline/Runtime','runtime_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'Runtime').glob('*.cs')},'validation':'not-yet-run'},indent=2)+'\n')
