using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GameplayTags;
internal static class GateCases
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Gate = typeof(TagStorage).GetMethod("CanOrderUnionContributions", F);
    static readonly FieldInfo Buckets = typeof(TagStorage).GetField("m_Buckets", F);
    static readonly FieldInfo Mask = typeof(TagStorage).GetField("m_BucketMask", F);
    static int cases;
    static int[] flatIds;
    static int[] chainIds;
    static byte[] Snapshot(TagStorage storage, int[] queue)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            var fields = typeof(TagStorage).GetFields(F);
            Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name,b.Name));
            foreach (var field in fields)
            {
                writer.Write(field.Name);
                object value = field.GetValue(storage);
                if (field.FieldType == typeof(int)) writer.Write((int)value);
                else if (field.FieldType.IsArray)
                {
                    var array = (Array)value; writer.Write(array.Length);
                    for (int i = 0; i < array.Length; i++)
                    {
                        object item = array.GetValue(i);
                        if (item is int) writer.Write((int)item);
                        else
                        {
                            var members = item.GetType().GetFields(F);
                            Array.Sort(members, (a,b) => string.CompareOrdinal(a.Name,b.Name));
                            foreach (var member in members)
                            {
                                if (member.FieldType != typeof(int)) throw new Exception("Unhandled field " + member.Name);
                                writer.Write((int)member.GetValue(item));
                            }
                        }
                    }
                }
                else throw new Exception("Unhandled storage field " + field.Name);
            }
            writer.Write("queue"); writer.Write(queue == null ? -1 : queue.Length);
            if (queue != null) foreach (int v in queue) writer.Write(v);
            writer.Flush(); return stream.ToArray();
        }
    }
    static string Hash(byte[] bytes)
    { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static bool Same(byte[] a, byte[] b)
    { if (a.Length != b.Length) return false; for (int i=0;i<a.Length;i++) if(a[i]!=b[i])return false; return true; }
    static void Check(string name, TagStorage storage, int[] queue, int queued, bool expected)
    {
        var arrays = new Dictionary<FieldInfo,object>();
        foreach (var f in typeof(TagStorage).GetFields(F)) if (f.FieldType.IsArray) arrays.Add(f, f.GetValue(storage));
        byte[] before = Snapshot(storage, queue);
        bool actual = (bool)Gate.Invoke(storage, new object[] { queue, queued });
        byte[] after = Snapshot(storage, queue);
        bool refs = true; foreach (var pair in arrays) refs &= ReferenceEquals(pair.Value, pair.Key.GetValue(storage));
        bool unchanged = Same(before, after);
        cases++;
        Console.WriteLine("CASE\t"+name+"\t"+expected+"\t"+actual+"\t"+unchanged+"\t"+refs+"\t"+before.Length+"\t"+Hash(before)+"\t"+Hash(after));
        if(actual!=expected || !unchanged || !refs) throw new Exception("Failed " + name);
    }
    static int Encode(int pending, int explicitBit=0) => unchecked((pending << 1) | explicitBit);
    static TagStorage Storage(params int[] pending)
    {
        var s = new TagStorage { Entries = new TagStorage.Entry[pending.Length+3], ExplicitIndices = new int[pending.Length+4], Count = pending.Length, ExplicitCount = pending.Length / 2 };
        for(int i=0;i<s.Entries.Length;i++)
            s.Entries[i] = new TagStorage.Entry { Id=i<pending.Length?flatIds[i]:100+i, Count=unchecked(int.MinValue+i*137), ExplicitCount=i<pending.Length?Encode(pending[i],i%2):unchecked((int)0x89abcdef), ExplicitIndex=i<pending.Length?i:-1-i };
        for(int i=0;i<s.ExplicitIndices.Length;i++)s.ExplicitIndices[i]=unchecked((int)0x11223344)+i;
        // Retain all original pending values, low bits, implicit indices, and poisoned tails.
        // Only the live explicit mapping is made valid for the new two-way preflight.
        for(int i=1;i<pending.Length;i+=2)
        {
            int slot=i/2; s.ExplicitIndices[slot]=i; s.Entries[i].ExplicitIndex=slot;
        }
        var buckets = new int[16]; for(int i=0;i<buckets.Length;i++)buckets[i]=unchecked((int)0x77889900)+i;
        Buckets.SetValue(s,buckets); Mask.SetValue(s,15);
        return s;
    }
    static int[] Queue(params int[] values)
    { var q = new int[values.Length+4]; Array.Copy(values,q,values.Length); for(int i=values.Length;i<q.Length;i++)q[i]=unchecked((int)0xded00000)+i; return q; }
    static void Main()
    {
        InitializeFixtures();
        if(Gate==null || Gate.IsGenericMethod)throw new Exception("Non-generic gate unavailable");
        Console.WriteLine("SNAPSHOT\tAll instance fields, full array capacities, array identity, and full queue; all values serialized as exact Int32 bytes");
        var s=Storage(1,2,3); Check("success-small",s,Queue(0,1,2),3,true);
        s=Storage(1,2,3); s.Entries[0].ExplicitIndex=-1; Check("preexisting-negative-first",s,Queue(0,1,2),3,false);
        s=Storage(1,2,3); s.Entries[2].ExplicitIndex=int.MinValue; Check("preexisting-negative-after-would-mark",s,Queue(0,1,2),3,false);
        s=Storage(1,2,0); s.Entries[2].ExplicitIndex=-17; Check("preexisting-negative-unqueued",s,Queue(0,1),2,false);
        Check("duplicate-after-one-mark",Storage(1,2),Queue(0,0),2,false);
        Check("duplicate-after-two-marks",Storage(1,2),Queue(0,1,0),3,false);
        Check("invalid-negative-first",Storage(1,2),Queue(-1,0),2,false);
        Check("invalid-negative-after-mark",Storage(1,2),Queue(0,-1),2,false);
        Check("invalid-count-after-mark",Storage(1,2),Queue(0,2),2,false);
        Check("invalid-max-after-two-marks",Storage(1,2),Queue(0,1,int.MaxValue),3,false);
        Check("queued-zero-first",Storage(0,1),Queue(0,1),2,false);
        Check("queued-zero-after-mark-explicit-bit",Storage(1,0),Queue(0,1),2,false);
        s=Storage(1,1,0); Check("queued-zero-after-two-marks-no-bit",s,Queue(0,1,2),3,false);
        Check("unqueued-high-bits",Storage(1,2,3),Queue(0,1),2,false);
        s=Storage(1,2,0); s.Entries[2].ExplicitCount=int.MinValue; Check("unqueued-sign-bit",s,Queue(0,1),2,false);
        Check("budget-overflow-two-2pow30",Storage(1<<30,1<<30),Queue(0,1),2,false);
        Check("budget-overflow-after-two-marks",Storage(1,1<<30,(1<<30)-1),Queue(0,1,2),3,false);
        Check("success-intmax-mass-split",Storage(1<<30,(1<<30)-1),Queue(0,1),2,true);
        Check("success-intmax-mass-one",Storage(int.MaxValue),Queue(0),1,true);
        s=Storage(1);s.Entries[0].ExplicitIndex=int.MaxValue;Check("success-marker-intmax-restored",s,Queue(0),1,true);
        s=Storage(1,2,0);s.Entries[0].Count=int.MinValue;s.Entries[1].Count=-17;Check("success-signed-negative-entry-counts",s,Queue(0,1),2,true);
        Check("success-zero-pending-unqueued",Storage(1,2,0,0),Queue(0,1),2,true);
        Check("empty-queue-empty-storage",Storage(),Queue(),0,true);
        Check("empty-queue-with-unqueued-highbits",Storage(1),Queue(),0,false);
        // A bounded combinatorial family targets unsigned shifts, sign-bit values,
        // budget limits, permutations and all prefix-restoration lengths.
        int[] weights={0,1,2,1<<29,(1<<30)-1,1<<30,int.MaxValue};
        foreach(int a in weights)foreach(int b in weights)
        {
            bool expected=a>0&&b>0&&(long)a+b<=int.MaxValue;
            Check("weights-forward-"+a+"-"+b,Storage(a,b),Queue(0,1),2,expected);
            Check("weights-reverse-"+a+"-"+b,Storage(a,b),Queue(1,0),2,expected);
        }
        PublicBulkNegative();
        MappingCases();
        ClosureCases();
        Console.WriteLine("SUMMARY\tcases="+cases+"\tpassed="+cases+"\tmutations=0\tarrayReplacements=0");
    }
    static void MappingCases()
    {
        // Start with three live explicit entries and distinct, valid reverse links.
        // All pending entries are queued. Any rejection below is mapping preflight,
        // before the first reversible queue marker in the frozen production method.
        int[] active={0,1,2,3,4,5};
        foreach(int slot in new[]{0,1,2})
        {
            int index=slot*2+1;
            foreach(int invalid in new[]{-1,6,8,int.MaxValue})
            {
                var s=Storage(1,2,3,4,5,6); s.ExplicitIndices[slot]=invalid;
                Check("mapping-forward-outofrange-slot-"+slot+"-value-"+invalid,s,Queue(active),6,false);
            }
            { var s=Storage(1,2,3,4,5,6); s.ExplicitIndices[slot]=index-1;
              Check("mapping-stale-to-live-implicit-slot-"+slot,s,Queue(active),6,false); }
            { var s=Storage(1,2,3,4,5,6); s.Entries[index].ExplicitCount&=~1;
              Check("mapping-missing-explicit-bit-slot-"+slot,s,Queue(active),6,false); }
            { var s=Storage(1,2,3,4,5,6); s.ExplicitIndices[slot]=((slot+1)%3)*2+1;
              Check("mapping-duplicate-forward-slot-"+slot,s,Queue(active),6,false); }
            foreach(int reverse in new[]{-1,3,int.MaxValue})
            {
                var s=Storage(1,2,3,4,5,6); s.Entries[index].ExplicitIndex=reverse;
                Check("mapping-invalid-reverse-slot-"+slot+"-value-"+reverse,s,Queue(active),6,false);
            }
            { var s=Storage(1,2,3,4,5,6); s.Entries[index].ExplicitIndex=(slot+1)%3;
              Check("mapping-wrong-reverse-slot-"+slot,s,Queue(active),6,false); }
        }
        { var s=Storage(1,2,3,4,5,6); s.ExplicitCount=2;
          Check("mapping-missing-final-forward-slot",s,Queue(active),6,false); }
        { var s=Storage(1,2,3,4,5,6); s.ExplicitCount=0;
          Check("mapping-missing-all-forward-slots",s,Queue(active),6,false); }
        foreach(int implicitIndex in new[]{0,2,4})
        {
            var s=Storage(1,2,3,4,5,6); s.Entries[implicitIndex].ExplicitCount|=1;
            Check("mapping-unmapped-explicit-entry-"+implicitIndex,s,Queue(active),6,false);
        }
        { var s=Storage(1,2,3,4,5,6); s.ExplicitIndices[0]=5;s.ExplicitIndices[2]=1;
          Check("mapping-swapped-forward-without-reverse",s,Queue(active),6,false); }
        // Fully consistent nonidentity mappings must pass, including sparse implicit
        // indices at Int32.MaxValue; only explicit entries own actual mapping slots.
        foreach(bool reverse in new[]{false,true})
        {
            var s=Storage(1,2,3,4,5,6);
            if(reverse) { s.ExplicitIndices[0]=5;s.ExplicitIndices[2]=1;s.Entries[1].ExplicitIndex=2;s.Entries[5].ExplicitIndex=0; }
            s.Entries[0].ExplicitIndex=int.MaxValue;s.Entries[2].ExplicitIndex=17;s.Entries[4].ExplicitIndex=6;
            Check("mapping-valid-nonidentity-"+reverse,s,Queue(5,3,1,4,2,0),6,true);
        }
        // These valid maps deliberately fail after every nonempty proper marker
        // prefix. Full-capacity snapshots and identities must remain unchanged.
        for(int prefix=1;prefix<=5;prefix++)
        {
            var values=new int[prefix+1]; for(int i=0;i<prefix;i++)values[i]=i;
            values[prefix]=0;
            Check("mapping-valid-duplicate-after-"+prefix+"-marks",Storage(1,2,3,4,5,6),Queue(values),values.Length,false);
            values[prefix]=int.MaxValue;
            Check("mapping-valid-outofrange-after-"+prefix+"-marks",Storage(1,2,3,4,5,6),Queue(values),values.Length,false);
            values[prefix]=prefix;
            var weights=new int[]{1,2,3,4,5,6};weights[prefix]=0;
            Check("mapping-valid-zero-after-"+prefix+"-marks",Storage(weights),Queue(values),values.Length,false);
        }
    }
    static void InitializeFixtures()
    {
        var registrations = new List<GameplayTagRegistration>();
        for(int i=0;i<8;i++) registrations.Add(new GameplayTagRegistration("GateRoot"+i));
        registrations.Add(new GameplayTagRegistration("Bulk.A.Leaf"));
        registrations.Add(new GameplayTagRegistration("Bulk.B.Leaf"));
        const int depth=1063;
        var names=new string[depth];names[0]="Closure";
        for(int i=1;i<depth;i++) names[i]=names[i-1]+".N";
        registrations.Add(new GameplayTagRegistration(names[depth-1]));
        GameplayTagManager.Initialize(registrations.ToArray());
        flatIds=new int[8];for(int i=0;i<8;i++)flatIds[i]=GameplayTagManager.RequestTag("GateRoot"+i).RuntimeIndex;
        chainIds=new int[depth];for(int i=0;i<depth;i++)chainIds[i]=GameplayTagManager.RequestTag(names[i]).RuntimeIndex;
    }
    static TagStorage ClosureStorage(int depth, int missing)
    {
        var s=new TagStorage();
        for(int i=0;i<depth;i++) if(i!=missing)
        {
            s.AddExplicit(chainIds[i]);
            s.AddTotal(chainIds[i],unchecked(int.MinValue+i*131));
        }
        for(int i=0;i<s.Count;i++)s.Entries[i].ExplicitCount=Encode(1,1);
        for(int i=s.Count;i<s.Entries.Length;i++)s.Entries[i]=new TagStorage.Entry{Id=unchecked((int)0xface0000)+i,Count=int.MinValue+i,ExplicitCount=-31-i,ExplicitIndex=int.MaxValue-i};
        for(int i=s.ExplicitCount;i<s.ExplicitIndices.Length;i++)s.ExplicitIndices[i]=unchecked((int)0xf00d0000)+i;
        return s;
    }
    static int[] ClosureQueue(TagStorage s)
    { var q=new int[s.Count+4];for(int i=0;i<s.Count;i++)q[i]=i;for(int i=s.Count;i<q.Length;i++)q[i]=int.MinValue+i;return q; }
    static void ClosureCases()
    {
        foreach(int depth in new[]{2,8,9,65,1063})
        {
            var s=ClosureStorage(depth,-1);Check("closure-complete-success-depth-"+depth,s,ClosureQueue(s),s.Count,true);
            s=ClosureStorage(depth,0);Check("closure-missing-root-depth-"+depth,s,ClosureQueue(s),s.Count,false);
            // A null queue would throw upon entering the marker loop. Returning
            // false proves the missing closure was rejected before queue access.
            s=ClosureStorage(depth,0);Check("closure-missing-root-pre-marker-depth-"+depth,s,null,s.Count,false);
        }
        foreach(int gap in new[]{1,7,8,31,512,1061})
        {
            var s=ClosureStorage(1063,gap);Check("closure-deep-gap-"+gap,s,ClosureQueue(s),s.Count,false);
            s=ClosureStorage(1063,gap);Check("closure-deep-gap-pre-marker-"+gap,s,null,s.Count,false);
        }
        // Present direct parent is insufficient unless its own parent is present.
        var leaf=ClosureStorage(3,0);Check("closure-missing-grandparent-pre-marker",leaf,null,leaf.Count,false);
    }
    static void PublicBulkNegative()
    {
        var counted=new GameplayTagCountContainer();counted.AddTag(GameplayTagManager.RequestTag("Bulk.A.Leaf"),3);counted.AddTag(GameplayTagManager.RequestTag("Bulk.B.Leaf"),7);
        var bulk=new GameplayTagContainer(counted);
        int negative=0;for(int i=0;i<bulk.Indices.Storage.Count;i++)if(bulk.Indices.Storage.Entries[i].ExplicitIndex<0)negative++;
        if(negative==0)throw new Exception("Public bulk construction did not produce negative parent link");
        Console.WriteLine("PUBLIC_BULK\tcounted-to-normal-copy\tnegativeExplicitIndices="+negative+"\ttags="+bulk.TagCount);
        Check("public-bulk-negative-link",bulk.Indices.Storage,Queue(bulk.Indices.Storage.ExplicitIndices[0]),1,false);
        var workspace=new GameplayTagIntersectionWorkspace();
        var unsorted=GameplayTagContainer.Intersection(counted,counted,workspace);
        negative=0;for(int i=0;i<unsorted.Indices.Storage.Count;i++)if(unsorted.Indices.Storage.Entries[i].ExplicitIndex<0)negative++;
        if(negative==0)throw new Exception("Public workspace construction did not produce negative parent link");
        Console.WriteLine("PUBLIC_BULK\tcounted-self-workspace-intersection\tnegativeExplicitIndices="+negative+"\ttags="+unsorted.TagCount);
        Check("public-unsorted-bulk-negative-link",unsorted.Indices.Storage,Queue(unsorted.Indices.Storage.ExplicitIndices[0]),1,false);
    }
}
