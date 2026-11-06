using System;
using System.IO;
using System.Reflection;
using ZwSoft.ZwCAD.Runtime;
using CadApp=ZwSoft.ZwCAD.ApplicationServices.Application;
[assembly:CommandClass(typeof(ZWLibraryHostCheck.Entry))]
namespace ZWLibraryHostCheck {
 public class Entry {
  [CommandMethod("ZWLIBMEMCHECK")]
  public void Run() {
   var config=File.ReadAllLines(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location,".txt"));
   try {
    var assembly=Assembly.LoadFrom(config[0]);
    if(!string.Equals(Path.GetFullPath(assembly.Location),Path.GetFullPath(config[0]),StringComparison.OrdinalIgnoreCase))throw new System.Exception("Test assembly path mismatch");
    var result=(string)assembly.GetType("ZWLibrary.Plugin").GetMethod("VerifyAgentInMemory",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
    File.WriteAllText(config[1],result);CadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n"+result+"\n");
   }catch(System.Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;File.WriteAllText(config[1],"FAIL: "+ex.GetType().Name+": "+ex.Message+"\n"+ex.StackTrace);}
  }
 }
}

