using System;
using System.Diagnostics;
using System.Globalization;
using GameplayTags;
internal static partial class Program {
 private static void LongBatchSmallCopy(){
  MakeBenchmarkInputs(4,50,true,out GameplayTagContainer a,out GameplayTagContainer ignored);
  var names=ExplicitNames(a);var copy=new GameplayTagContainer();GameplayTagContainer.Copy(copy,a);
  Check(ExplicitNames(copy).SetEquals(names)&&copy.TagCount==a.TagCount,"supplemental small Copy preflight");
  Measure("E4.Copy.Reused.Supplemental",()=>{GameplayTagContainer.Copy(copy,a);s_Sink+=copy.TagCount;},10000000);
  Check(ExplicitNames(copy).SetEquals(names)&&copy.TagCount==a.TagCount&&ExplicitNames(a).SetEquals(names),"supplemental small Copy postflight");
 }
}
