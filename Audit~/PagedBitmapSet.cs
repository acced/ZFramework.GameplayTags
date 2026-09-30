// Experimental kernel only. Not included in the Unity Runtime assembly.
// Each build fixes ONE bitmap page format; there is no member-ID-list fallback.
using System;
using System.Runtime.CompilerServices;
using GameplayTags;

namespace PageExperiment
{
    public sealed class PagedBitmapSet
    {
#if PAGE128
        public const int PageShift = 7;
#elif PAGE256
        public const int PageShift = 8;
#elif PAGE512
        public const int PageShift = 9;
#else
        public const int PageShift = 6;
#endif
        public const int PageBits = 1 << PageShift;
        private const int Words = PageBits / 64;
        private const int Stride = Words + 1; // key + page population in header, followed by fixed bitmap words
        private const int BranchSize = 65;  // active slot count/free link + 64 DIRECT child slots
        private readonly int roots, maxPages;
        private int[] map;
        private ulong[] data;
        private int pages, count, usedBranches, activeBranches, freeBranch;
        private int inlineKey = -1;
        private ulong i0;
#if PAGE128 || PAGE256 || PAGE512
        private ulong i1;
#endif
#if PAGE256 || PAGE512
        private ulong i2, i3;
#endif
#if PAGE512
        private ulong i4, i5, i6, i7;
#endif
        public TagRegistry Registry { get; }
        public int Count => count;
        public int ActivePages => pages;
        public bool IsEmpty => count == 0;
        public static string Label => "direct-page-" + PageBits;
        public long BufferBytes => Words * 8L + (map == null ? 0 : 4L * map.Length) + (data == null ? 0 : 8L * data.Length);
        private int PageCapacity => data == null ? Math.Min(1, maxPages) : data.Length / Stride;
        private int BranchCapacity => map == null ? 0 : (map.Length - roots) / BranchSize;

        public PagedBitmapSet(TagRegistry registry, int capacity = 0)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            maxPages = (int)(((long)registry.Count + PageBits - 1) >> PageShift);
            roots = (maxPages + 63) >> 6;
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (capacity > 1) EnsureCapacity(capacity);
        }
        public PagedBitmapSet(PagedBitmapSet source) : this(Required(source).Registry)
        {
            if (source.pages <= 1) { CopyInline(source); return; }
            Reserve(source.pages, source.activeBranches, true);
            CopyContents(source);
        }
        private static PagedBitmapSet Required(PagedBitmapSet set) => set ?? throw new ArgumentNullException(nameof(set));
        private void Require(PagedBitmapSet other) { Required(other); Registry.RequireSame(other.Registry); }
        private bool Accept(RuntimeTag tag)
        {
            if (ReferenceEquals(Registry, tag.Owner)) return true;
            if (tag.Owner == null) return false;
            Registry.RequireSame(tag.Owner); return true;
        }
        public void EnsureCapacity(int members)
        {
            if (members < 0) throw new ArgumentOutOfRangeException(nameof(members));
            int required = Math.Min(members, maxPages);
            if (required > 1) Reserve(required, Math.Min(required, roots), true);
        }
        // Reserve for arbitrary placement. Existing pages retain physical slots during growth.
        private void Reserve(int pageCount, int branchCount, bool exact = false)
        {
            if (pageCount <= 1 && map == null) return;
            pageCount = Math.Min(pageCount, maxPages);
            branchCount = Math.Min(branchCount, roots);
            if (map == null)
            {
                int pc = exact ? pageCount : Math.Min(maxPages, Math.Max(4, pageCount));
                int bc = exact ? branchCount : Math.Min(roots, Math.Max(2, branchCount));
                map = new int[checked(roots + bc * BranchSize)];
                data = new ulong[checked(pc * Stride)];
                if (pages != 0)
                {
                    data[0] = Header(inlineKey, count);
                    for (int w = 0; w < Words; w++) data[w + 1] = ReadInline(w);
                    MapSet(inlineKey, 0);
                }
                inlineKey = -1; ClearInline();
                return;
            }
            if (branchCount > BranchCapacity)
            {
                int bc = exact ? branchCount : (int)Math.Min(roots, Math.Max(branchCount, 2L * BranchCapacity));
                Array.Resize(ref map, checked(roots + bc * BranchSize));
            }
            if (pageCount > PageCapacity)
            {
                int pc = exact ? pageCount : (int)Math.Min(maxPages, Math.Max(pageCount, 2L * PageCapacity));
                Array.Resize(ref data, checked(pc * Stride));
            }
        }
        private static ulong Header(int key, int population) => ((ulong)(uint)population << 32) | (uint)key;
        private int Key(int slot) => map == null ? inlineKey : (int)(uint)data[slot * Stride];
        private ulong Word(int slot, int word) => map == null ? ReadInline(word) : data[slot * Stride + 1 + word];
        private int Population(int slot) => map == null ? count : (int)(data[slot * Stride] >> 32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ReadInline(int word)
        {
#if PAGE512
            switch (word) { case 0:return i0;case 1:return i1;case 2:return i2;case 3:return i3;case 4:return i4;case 5:return i5;case 6:return i6;default:return i7; }
#elif PAGE256
            switch (word) { case 0:return i0;case 1:return i1;case 2:return i2;default:return i3; }
#elif PAGE128
            return word == 0 ? i0 : i1;
#else
            return i0;
#endif
        }
        private void WriteInline(int word, ulong value)
        {
#if PAGE512
            switch(word){case 0:i0=value;break;case 1:i1=value;break;case 2:i2=value;break;case 3:i3=value;break;case 4:i4=value;break;case 5:i5=value;break;case 6:i6=value;break;default:i7=value;break;}
#elif PAGE256
            switch(word){case 0:i0=value;break;case 1:i1=value;break;case 2:i2=value;break;default:i3=value;break;}
#elif PAGE128
            if(word==0)i0=value;else i1=value;
#else
            i0=value;
#endif
        }
        private void ClearInline() { for (int w = 0; w < Words; w++) WriteInline(w, 0); }
        private int FindPage(int key)
        {
            if (map == null) return pages != 0 && inlineKey == key ? 0 : -1;
            int node = map[key >> 6];
            return node == 0 ? -1 : map[node + 1 + (key & 63)] - 1;
        }
        // The map stores page addresses, never individual member IDs. No rank or binary search.
        private void MapSet(int key, int slot)
        {
            int root = key >> 6, node = map[root];
            if (node == 0)
            {
                if (freeBranch != 0) { node = freeBranch; freeBranch = map[node]; }
                else node = roots + usedBranches++ * BranchSize;
                map[node] = 0; map[root] = node; activeBranches++;
                // Child slots are already zero: fresh allocation, removed last child, or Clear().
            }
            int at = node + 1 + (key & 63);
            if (map[at] == 0) map[node]++;
            map[at] = slot + 1;
        }
        private void MapRemove(int key)
        {
            int root = key >> 6, node = map[root];
            map[node + 1 + (key & 63)] = 0;
            if (--map[node] == 0)
            { map[root] = 0; map[node] = freeBranch; freeBranch = node; activeBranches--; }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTagExact(RuntimeTag tag)
        {
            if (!ReferenceEquals(Registry, tag.Owner))
            { if (tag.Owner == null) return false; Registry.RequireSame(tag.Owner); }
            int id = tag.Id, key = id >> PageShift;
            if (map == null) return pages != 0 && inlineKey == key && (ReadInline((id & (PageBits - 1)) >> 6) & (1UL << id)) != 0;
            int node = map[key >> 6];
            if (node == 0) return false;
            int slot = map[node + 1 + (key & 63)] - 1;
            return slot >= 0 && (data[slot * Stride + 1 + ((id & (PageBits - 1)) >> 6)] & (1UL << id)) != 0;
        }
        public bool AddTag(RuntimeTag tag)
        {
            if (!Accept(tag)) return false;
            int key = tag.Id >> PageShift, w = (tag.Id & (PageBits - 1)) >> 6;
            ulong bit = 1UL << tag.Id;
            if (map == null && (pages == 0 || key == inlineKey))
            {
                ulong before = ReadInline(w);
                if ((before & bit) != 0) return false;
                inlineKey = key; pages = 1; WriteInline(w, before | bit); count++; return true;
            }
            int slot = FindPage(key);
            if (slot < 0)
            {
                int branches = activeBranches + (map == null || map[key >> 6] == 0 ? 1 : 0);
                // Include the inline source's branch when materializing it.
                if (map == null) branches = (inlineKey >> 6) == (key >> 6) ? 1 : 2;
                Reserve(pages + 1, branches);
                slot = pages++; int start = slot * Stride;
                Array.Clear(data, start, Stride); data[start] = (uint)key; MapSet(key, slot);
            }
            int at = slot * Stride + 1 + w;
            ulong old = data[at]; if ((old & bit) != 0) return false;
            data[at] = old | bit; data[slot * Stride] += 1UL << 32; count++; return true;
        }
        public bool RemoveTag(RuntimeTag tag)
        {
            if (!Accept(tag)) return false;
            int key = tag.Id >> PageShift, slot = FindPage(key);
            if (slot < 0) return false;
            int w = (tag.Id & (PageBits - 1)) >> 6; ulong bit = 1UL << tag.Id;
            if (map == null)
            {
                ulong before = ReadInline(w); if ((before & bit) == 0) return false;
                WriteInline(w, before & ~bit); if (--count == 0) { pages = 0; inlineKey = -1; } return true;
            }
            int start = slot * Stride, at = start + 1 + w;
            if ((data[at] & bit) == 0) return false;
            data[at] &= ~bit; data[start] -= 1UL << 32; count--;
            if ((data[start] >> 32) == 0) DeletePage(slot);
            return true;
        }
        private void DeletePage(int slot)
        {
            int key = (int)(uint)data[slot * Stride]; MapRemove(key);
            int last = --pages;
            if (slot != last)
            {
                Array.Copy(data, last * Stride, data, slot * Stride, Stride);
                MapSet((int)(uint)data[slot * Stride], slot);
            }
        }
        public void Clear()
        {
            if (map == null) { ClearInline(); inlineKey = -1; pages = count = 0; return; }
            // Clear just the old active mappings, not the registry-sized root or every reserved page.
            for (int p = 0; p < pages; p++)
            { int key = Key(p), node = map[key >> 6]; map[node + 1 + (key & 63)] = 0; }
            for (int p = 0; p < pages; p++) map[Key(p) >> 6] = 0;
            pages = count = usedBranches = activeBranches = freeBranch = 0;
        }
        private void CopyInline(PagedBitmapSet source)
        {
            ClearInline(); count = source.count; pages = source.pages;
            inlineKey = pages == 0 ? -1 : source.Key(0);
            if (pages != 0) for (int w = 0; w < Words; w++) WriteInline(w, source.Word(0, w));
        }
        public void CopyFrom(PagedBitmapSet source)
        {
            Require(source); if (ReferenceEquals(this, source)) return;
            if (map == null && source.pages <= 1) { CopyInline(source); return; }
            if (source.pages == 0) { Clear(); return; }
            Reserve(source.pages, source.map == null ? 1 : source.activeBranches);
            Clear(); CopyContents(source);
        }
        private void CopyContents(PagedBitmapSet source)
        {
            if (map == null) { CopyInline(source); return; }
            if (source.map != null) Array.Copy(source.data, data, source.pages * Stride);
            else if (source.pages != 0)
            { data[0] = Header(source.inlineKey, source.count); for (int w=0;w<Words;w++) data[1+w]=source.ReadInline(w); }
            pages = source.pages; count = source.count;
            for (int p = 0; p < pages; p++) MapSet(Key(p), p);
        }
        public static PagedBitmapSet Union(PagedBitmapSet left, PagedBitmapSet right)
        {
            Required(left).Require(right);
            if (ReferenceEquals(left, right) || right.pages == 0) return new PagedBitmapSet(left);
            if (left.pages == 0) return new PagedBitmapSet(right);
            var result = new PagedBitmapSet(left.Registry);
            if (left.pages == 1 && right.pages == 1 && left.Key(0) == right.Key(0))
            {
                result.inlineKey = left.Key(0); result.pages = 1;
                for (int w=0;w<Words;w++) { ulong v=left.Word(0,w)|right.Word(0,w);result.WriteInline(w,v);result.count+=Pop(v); }
                return result;
            }
            int pc = Math.Min(result.maxPages, left.pages + right.pages);
            int bc = Math.Min(result.roots, (left.map == null ? 1 : left.activeBranches) + (right.map == null ? 1 : right.activeBranches));
#if EXACT_RESERVE
            pc = left.pages;
            for (int p=0;p<right.pages;p++) if(left.FindPage(right.Key(p))<0) pc++;
            bc = 0;
            for(int r=0;r<result.roots;r++)
                if ((left.map == null ? (left.inlineKey >> 6) == r : left.map[r] != 0)
                    || (right.map == null ? (right.inlineKey >> 6) == r : right.map[r] != 0)) bc++;
#endif
            result.Reserve(pc, bc, true); result.CopyContents(left); result.AppendCore(right); return result;
        }
        public static void UnionInto(PagedBitmapSet left, PagedBitmapSet right, PagedBitmapSet result)
        {
            Required(left).Require(right); left.Require(result);
            if (ReferenceEquals(result,left)) { result.AppendTags(right); return; }
            if (ReferenceEquals(result,right)) { result.AppendTags(left); return; }
            result.CopyFrom(left); result.AppendCore(right);
        }
        public void AppendTags(PagedBitmapSet other)
        { Require(other); if (!ReferenceEquals(this,other)) AppendCore(other); }
        private void AppendCore(PagedBitmapSet other)
        {
            if (other.pages == 0) return;
            if (pages == 0) { CopyFrom(other); return; }
            for (int p=0;p<other.pages;p++)
            {
                int key=other.Key(p), slot=FindPage(key);
                if(map==null && slot==0)
                {
                    for(int w=0;w<Words;w++) { ulong a=ReadInline(w),b=other.Word(p,w);count+=Pop(b&~a);WriteInline(w,a|b); }
                    continue;
                }
                if(slot<0)
                {
                    int bc=map==null ? ((inlineKey>>6)==(key>>6)?1:2) : activeBranches+(map[key>>6]==0?1:0);
                    Reserve(pages+1,bc); slot=pages++;
                    int start=slot*Stride,pop=other.Population(p);
                    data[start]=Header(key,pop);
                    for(int w=0;w<Words;w++)data[start+1+w]=other.Word(p,w);
                    MapSet(key,slot);count+=pop;continue;
                }
                int begin=slot*Stride,added=0;
                for(int w=0;w<Words;w++) { ulong a=data[begin+1+w],b=other.Word(p,w);added+=Pop(b&~a);data[begin+1+w]=a|b; }
                data[begin]+=(ulong)(uint)added<<32;count+=added;
            }
        }
        public void RemoveTags(PagedBitmapSet other)
        {
            Require(other); if(ReferenceEquals(this,other)){Clear();return;}
            for(int p=0;p<other.pages;p++)
            {
                int slot=FindPage(other.Key(p));if(slot<0)continue;
                if(map==null)
                {
                    for(int w=0;w<Words;w++){ulong a=ReadInline(w),b=other.Word(p,w);count-=Pop(a&b);WriteInline(w,a&~b);}
                    if(count==0){pages=0;inlineKey=-1;}continue;
                }
                int start=slot*Stride,removed=0;
                for(int w=0;w<Words;w++){ulong a=data[start+1+w],b=other.Word(p,w);removed+=Pop(a&b);data[start+1+w]=a&~b;}
                data[start]-=(ulong)(uint)removed<<32;count-=removed;
                if((data[start]>>32)==0)DeletePage(slot);
            }
        }
        public static void IntersectionExactInto(PagedBitmapSet left, PagedBitmapSet right, PagedBitmapSet result)
        {
            Required(left).Require(right);left.Require(result);
            if(ReferenceEquals(result,right)){result.IntersectWith(left);return;}
            if(!ReferenceEquals(result,left))result.CopyFrom(left);
            result.IntersectWith(right);
        }
        private void IntersectWith(PagedBitmapSet other)
        {
            if(ReferenceEquals(this,other))return;
            for(int p=pages-1;p>=0;p--)
            {
                int q=other.FindPage(Key(p)),before=Population(p),after=0;
                if(map==null)
                {
                    for(int w=0;w<Words;w++){ulong v=q<0?0:ReadInline(w)&other.Word(q,w);WriteInline(w,v);after+=Pop(v);}
                    count=after;if(after==0){pages=0;inlineKey=-1;}return;
                }
                int start=p*Stride;
                for(int w=0;w<Words;w++){ulong v=q<0?0:data[start+1+w]&other.Word(q,w);data[start+1+w]=v;after+=Pop(v);}
                data[start]=Header(Key(p),after);count+=after-before;
                if(after==0)DeletePage(p);
            }
        }
        // No hardware-only dependency; the JIT/IL2CPP may recognize this standard SWAR form.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pop(ulong x)
        { x-= (x>>1)&0x5555555555555555UL;x=(x&0x3333333333333333UL)+((x>>2)&0x3333333333333333UL);x=(x+(x>>4))&0x0F0F0F0F0F0F0F0FUL;return (int)((x*0x0101010101010101UL)>>56); }
        public Enumerator GetEnumerator()=>new Enumerator(this);
        public struct Enumerator
        {
            private readonly PagedBitmapSet set;
            private int nextWord, baseId;
            private ulong remaining;
            public RuntimeTag Current { get; private set; }
            internal Enumerator(PagedBitmapSet set){this.set=set;nextWord=baseId=0;remaining=0;Current=default;}
            public bool MoveNext()
            {
                while(remaining==0)
                {
                    if(nextWord>=set.pages*Words)return false;
                    int slot=nextWord/Words,w=nextWord%Words;nextWord++;
                    baseId=(set.Key(slot)<<PageShift)+(w<<6);remaining=set.Word(slot,w);
                }
                ulong bit=remaining&unchecked(0UL-remaining);int index=0;
                // Enumeration is not on the measured four hot paths; portable zero-allocation scan.
                if((bit&0xFFFFFFFFUL)==0){index+=32;bit>>=32;}if((bit&0xFFFF)==0){index+=16;bit>>=16;}
                if((bit&0xFF)==0){index+=8;bit>>=8;}if((bit&0xF)==0){index+=4;bit>>=4;}
                if((bit&3)==0){index+=2;bit>>=2;}if((bit&1)==0)index++;
                remaining&=remaining-1;Current=new RuntimeTag(set.Registry,baseId+index);return true;
            }
        }
        public void CheckInvariants()
        {
            int total=0;
            for(int p=0;p<pages;p++)
            {
                int key=Key(p),pop=0;
                if(key<0||key>=maxPages||FindPage(key)!=p)throw new Exception("Page mapping");
                for(int w=0;w<Words;w++)pop+=Pop(Word(p,w));
                if(pop==0||pop!=Population(p))throw new Exception("Page population");
                total+=pop;
            }
            if(total!=count)throw new Exception("Total population");
            if(map==null){if(pages>1||((pages==0)!=(inlineKey<0)))throw new Exception("Inline state");return;}
            int active=0,slots=0;
            for(int r=0;r<roots;r++)
            {
                int node=map[r];if(node==0)continue;active++;int n=0;
                if(node<roots||(node-roots)%BranchSize!=0||node+BranchSize>map.Length)throw new Exception("Branch address");
                for(int s=0;s<64;s++) { int p=map[node+1+s]-1;if(p<0)continue;n++;if(p>=pages||Key(p)!=(r<<6|s))throw new Exception("Back mapping"); }
                if(n==0||map[node]!=n)throw new Exception("Branch population");slots+=n;
            }
            if(active!=activeBranches||slots!=pages)throw new Exception("Directory population");
            int free=0;
            for(int node=freeBranch;node!=0;node=map[node])
            {if(++free>usedBranches)throw new Exception("Free cycle");for(int i=1;i<BranchSize;i++)if(map[node+i]!=0)throw new Exception("Dirty free branch");}
            if(free+active!=usedBranches)throw new Exception("Branch ownership");
        }
    }
}
