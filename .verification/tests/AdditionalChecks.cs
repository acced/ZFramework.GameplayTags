using System;
using System.Collections.Generic;
using System.Linq;
using GameplayTags;

public static class AdditionalChecks
{
    public static long Assertions;
    static void Check(bool value,string message){Assertions++;if(!value)throw new Exception(message);}
    public static void Run()
    {
        foreach(int total in new[]{100,700})
        {
            GameplayTagManager.Initialize(Enumerable.Range(0,total).Select(i=>new GameplayTagRegistration("T"+i.ToString("D3"))).ToArray());
            var tags=GameplayTagManager.GetAllTags().ToArray();
            var holder=new GameplayTagCountContainer();var active=new bool[30];var handlers=new OnTagCountChangedDelegate[30];
            var log=new List<int>();var random=new Random(total);
            for(int i=0;i<30;i++){int index=i;handlers[i]=(t,n)=>{Check(t==tags[index],"indexed callback tag");log.Add(index);};}
            for(int step=0;step<1500;step++)
            {
                int changed=random.Next(30);active[changed]=!active[changed];
                if(active[changed])holder.RegisterTagEventCallback(tags[changed],GameplayTagEventType.AnyCountChange,handlers[changed]);
                else holder.RemoveTagEventCallback(tags[changed],GameplayTagEventType.AnyCountChange,handlers[changed]);
                // Every subscription state is probed, including removed and shifted slots.
                for(int i=0;i<30;i++)
                {
                    log.Clear();holder.AddTag(tags[i]);holder.RemoveTag(tags[i]);
                    Check(active[i] ? log.Count==2 && log[0]==i && log[1]==i : log.Count==0,"subscription churn/threshold");
                }
            }
            // Removal during dispatch must not alter the already queued callback snapshot.
            holder.RemoveAllTagEventCallbacks();var sequence=new List<int>();
            OnTagCountChangedDelegate later=(t,n)=>sequence.Add(2);
            OnTagCountChangedDelegate earlier=(t,n)=>{sequence.Add(1);holder.RemoveTagEventCallback(t,GameplayTagEventType.AnyCountChange,later);};
            holder.RegisterTagEventCallback(tags[0],GameplayTagEventType.NewOrRemoved,earlier);
            holder.RegisterTagEventCallback(tags[0],GameplayTagEventType.AnyCountChange,later);
            holder.AddTag(tags[0]);Check(sequence.SequenceEqual(new[]{1,2}),"snapshot survives unsubscribe during dispatch");
        }
    }
}
