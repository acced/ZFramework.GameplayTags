from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]; snippets=Path(__file__).parent/'docs'
def section(text, name, content):
 pat=rf'(<a name="{re.escape(name)}"></a>\n)(.*?)(?=<a name="|\Z)'
 text,n=re.subn(pat,lambda m:m[1]+content,text, count=1,flags=re.S)
 if n!=1:raise RuntimeError('Missing documentation anchor '+name)
 return text
for filename, langs in [('README.md',[('en',''),('cn','cn-')]),('README_CN.md',[('cn','')])]:
 p=root/filename;s=p.read_text()
 for lang,prefix in langs:
  for anchor in ['queries','pooling']:
   s=section(s,prefix+anchor,(snippets/(anchor+'-'+lang+'.md')).read_text())
  marker='<a name="'+prefix+'caller-contract"></a>'
  assert s.count(marker)==1
  s=s.replace(marker,'<a name="'+prefix+'player-registry"></a>\n'+(snippets/('player-registry-'+lang+'.md')).read_text()+marker)
 # Keep existing prose and homepage language navigation; update only changed usage.
 s=s.replace('GameplayTagContainer.Copy(output, left);', 'GameplayTagContainer.Copy(output, left);')
 # Existing Copy+AddTags example is valid; add alias-safe direct union beside it.
 lines=s.splitlines();out=[]
 for line in lines:
  out.append(line)
  if 'output.AddTags(right);' in line:
   out.append('GameplayTagContainer.Union(output, left, right);      // Replaces output; either input may be output.')
 s='\n'.join(out)+'\n'
 s=s.replace('2.1.0-rc.2`','2.1.0-rc.2-optimized.2`')
 # Caches are not beforefieldinit: no change to runtime, only the application example.
 s=s.replace('public static class GameTags\n{','public static class GameTags\n{\n    static GameTags() { }')
 p.write_text(s)
