#!/usr/bin/env python3
"""Experimental always-live inline prefix + cursor kernels; never changes shipping source."""
import pathlib
import packed_experiment as experiment
ROOT=pathlib.Path(__file__).resolve().parent.parent
cursor=experiment.proposed
original_project=experiment.project

def replace_once(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Reconcile prefix experiment: '+old[:100])
    return text.replace(old,new)

def proposed(base):
    text=cursor(base)
    text=replace_once(text,'private Group[] m_Groups;','private readonly int m_RegistryWords;\n        private Group[] m_Groups;')
    text=text.replace('Registry.WordCount','m_RegistryWords')
    text=replace_once(text,'Registry = registry ?? throw new ArgumentNullException(nameof(registry));','Registry = registry ?? throw new ArgumentNullException(nameof(registry));\n            m_RegistryWords = registry.WordCount;')
    text=replace_once(text,'Registry = Required(source).Registry;','Registry = Required(source).Registry;\n            m_RegistryWords = source.m_RegistryWords;')
    text=replace_once(text,'private int MaxGroups => (int)(((long)Registry.Count + 4095) >> 12);','private int MaxGroups => (m_RegistryWords + 63) >> 6;')
    text=replace_once(text,'m_Groups == null ? Math.Min(1, MaxGroups) : m_Groups.Length;','m_Groups == null ? Math.Min(1, MaxGroups) : m_Groups.Length + 1;')
    text=replace_once(text,'m_Words == null ? Math.Min(1, m_RegistryWords) : m_Words.Length;','m_Words == null ? Math.Min(1, m_RegistryWords) : m_Words.Length + 1;')
    text=replace_once(text,'private Group ReadGroup(int index) => m_Groups == null ? m_InlineGroup : m_Groups[index];','private Group ReadGroup(int index) => index == 0 ? m_InlineGroup : m_Groups[index - 1];')
    text=replace_once(text,'private ulong ReadWord(int index) => m_Words == null ? m_InlineWord : m_Words[index];','private ulong ReadWord(int index) => index == 0 ? m_InlineWord : m_Words[index - 1];')
    text=replace_once(text,'if (m_Groups == null) m_InlineGroup = value; else m_Groups[index] = value;','if (index == 0) m_InlineGroup = value; else m_Groups[index - 1] = value;')
    text=replace_once(text,'if (m_Words == null) m_InlineWord = value; else m_Words[index] = value;','if (index == 0) m_InlineWord = value; else m_Words[index - 1] = value;')
    text=replace_once(text,'var buffer = new Group[size];','var buffer = new Group[size - 1];')
    text=replace_once(text,'else buffer[0] = m_InlineGroup;','// The first directory remains inline, not duplicated in the tail.')
    text=replace_once(text,'var buffer = new ulong[size];','var buffer = new ulong[size - 1];')
    text=replace_once(text,'else buffer[0] = m_InlineWord;','// The first member word remains inline, not duplicated in the tail.')
    text=replace_once(text,'if (at < m_GroupCount) Array.Copy(m_Groups, at, m_Groups, at + 1, m_GroupCount - at);','''if (at == 0 && m_GroupCount != 0)
                {
                    if (m_GroupCount > 1) Array.Copy(m_Groups, 0, m_Groups, 1, m_GroupCount - 1);
                    m_Groups[0] = m_InlineGroup;
                }
                else if (at < m_GroupCount) Array.Copy(m_Groups, at - 1, m_Groups, at, m_GroupCount - at);''')
    text=replace_once(text,'if (at < m_WordCount) Array.Copy(m_Words, at, m_Words, at + 1, m_WordCount - at);','''if (at == 0 && m_WordCount != 0)
            {
                if (m_WordCount > 1) Array.Copy(m_Words, 0, m_Words, 1, m_WordCount - 1);
                m_Words[0] = m_InlineWord;
            }
            else if (at < m_WordCount) Array.Copy(m_Words, at - 1, m_Words, at, m_WordCount - at);''')
    text=replace_once(text,'if (offset < m_WordCount) Array.Copy(m_Words, offset + 1, m_Words, offset, m_WordCount - offset);','''if (offset == 0 && m_WordCount != 0)
            {
                m_InlineWord = m_Words[0];
                if (m_WordCount > 1) Array.Copy(m_Words, 1, m_Words, 0, m_WordCount - 1);
            }
            else if (offset < m_WordCount) Array.Copy(m_Words, offset, m_Words, offset - 1, m_WordCount - offset);''')
    text=replace_once(text,'if (at < m_GroupCount) Array.Copy(m_Groups, at + 1, m_Groups, at, m_GroupCount - at);','''if (at == 0 && m_GroupCount != 0)
                {
                    m_InlineGroup = m_Groups[0];
                    if (m_GroupCount > 1) Array.Copy(m_Groups, 1, m_Groups, 0, m_GroupCount - 1);
                }
                else if (at < m_GroupCount) Array.Copy(m_Groups, at, m_Groups, at - 1, m_GroupCount - at);''')
    text=experiment.method(text,'private void CopyContents(RuntimeTagSet source)','''private void CopyContents(RuntimeTagSet source)
        {
            if (source.m_GroupCount != 0)
            {
                m_InlineGroup = source.m_InlineGroup; m_InlineWord = source.m_InlineWord;
                if (source.m_GroupCount > 1) Array.Copy(source.m_Groups, m_Groups, source.m_GroupCount - 1);
                if (source.m_WordCount > 1) Array.Copy(source.m_Words, m_Words, source.m_WordCount - 1);
            }
            m_GroupCount = source.m_GroupCount; m_WordCount = source.m_WordCount; m_Count = source.m_Count;
        }''')
    text=experiment.method(text,'private static void CopyWords(','private static void CopyWords(RuntimeTagSet source, int sourceStart, RuntimeTagSet result, int destinationStart, int count)\n        {\n            if (sourceStart == 0 || destinationStart == 0)\n            {\n                ulong first = source.ReadWord(sourceStart);\n                if (count > 1) Array.Copy(source.m_Words, sourceStart, result.m_Words, destinationStart, count - 1);\n                result.WriteWord(destinationStart, first);\n            }\n            else Array.Copy(source.m_Words, sourceStart - 1, result.m_Words, destinationStart - 1, count);\n        }')
    text=replace_once(text,'return ContainsId(tag.Id);','''// If every logical word is present, packed position equals logical position.
            // No extra member cache, representation conversion or directory lookup is required.
            if (m_WordCount == m_RegistryWords)
                return (ReadWord(tag.Id >> 6) & (1UL << (tag.Id & 63))) != 0;
            return ContainsId(tag.Id);''')
    text=replace_once(text,'group.Mask == ulong.MaxValue ? slot','(group.Mask & (wordBit - 1)) == wordBit - 1 ? slot')
    return text

def checked_project(path,includes,*args,**kwargs):
    includes=list(includes)
    target=ROOT/'Audit~/PackedTests.cs'
    if target in includes:
        source=target.read_text()
        source=replace_once(source,'        Tests();','        Tests(); PackedLayoutChecks.Run();')
        patched=path.parent/'PackedTestsWithLayoutChecks.cs';patched.write_text(source)
        includes[includes.index(target)]=patched
        includes.append(ROOT/'Audit~/PackedLayoutChecks.cs')
    return original_project(path,includes,*args,**kwargs)

if __name__=='__main__':
    experiment.proposed=proposed
    experiment.project=checked_project
    experiment.main()
