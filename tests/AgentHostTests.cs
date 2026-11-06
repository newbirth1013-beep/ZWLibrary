using System;
using System.Collections.Generic;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
namespace ZWLibrary {
 public partial class Plugin {
  public static string VerifyAgentInMemory() {
   int passed=0;
   var previous=HostApplicationServices.WorkingDatabase;
   try { using(var db=new Database(true,true)) {
    HostApplicationServices.WorkingDatabase=db;
    ObjectId first,other;
    using(var tr=db.TransactionManager.StartTransaction()) {
     var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForWrite);
     var def=new BlockTableRecord {Name="ZWAI_TEST_FIXTURE"};table.Add(def);tr.AddNewlyCreatedDBObject(def,true);
     var ad=new AttributeDefinition {Tag="UNIT_NO",TextString="CB01",Position=Point3d.Origin,Height=2};
     def.AppendEntity(ad);tr.AddNewlyCreatedDBObject(ad,true);
     var model=(BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
     first=MakeFixture(model,def,ad,"CB01",tr);other=MakeFixture(model,def,ad,"CB02",tr);tr.Commit();
    }
    var original=FixtureInstance(db,first);
    ApplyAgentPlan(db,new AgentPlan {action="rename",handle=original.PhysicalKey,old_tag="CB01",new_tag="CB09"},original);
    if(FixtureInstance(db,first).Tag!="CB09" || FixtureInstance(db,other).Tag!="CB02")throw new Exception("rename/other-object verification failed");passed++;
    var current=FixtureInstance(db,first);bool rejected=false;
    try{ApplyAgentPlan(db,new AgentPlan {action="rename",handle=current.PhysicalKey,old_tag="CB09",new_tag="CB02"},current);}catch(InvalidOperationException){rejected=true;}
    if(!rejected||FixtureInstance(db,first).Tag!="CB09")throw new Exception("duplicate-number rejection/rollback failed");passed++;
    rejected=false;
    try{ApplyAgentPlan(db,new AgentPlan {action="rename",handle=current.PhysicalKey,old_tag="STALE",new_tag="CB10"},current);}catch(InvalidOperationException){rejected=true;}
    if(!rejected||FixtureInstance(db,first).Tag!="CB09")throw new Exception("stale snapshot rejection failed");passed++;
    ApplyAgentPlan(db,new AgentPlan {action="rename",handle=current.PhysicalKey,old_tag="CB09",new_tag="CB01"},current);
    if(FixtureInstance(db,first).Tag!="CB01")throw new Exception("reverse edit failed");passed++;
    using(var tr=db.TransactionManager.StartTransaction()) {
     var br=(BlockReference)tr.GetObject(first,OpenMode.ForWrite);var data=ReadCurrentInstance(br,tr);data.LinkId="fixture-link";WriteInstance(br,data,tr);tr.Commit();
    }
    var linked=FixtureInstance(db,first);rejected=false;
    try{ApplyAgentPlan(db,new AgentPlan {action="rename",handle=linked.PhysicalKey,old_tag="CB01",new_tag="CB10"},linked);}catch(InvalidOperationException){rejected=true;}
    if(!rejected||FixtureInstance(db,first).Tag!="CB01")throw new Exception("linked-part guard failed");passed++;
   }
   } finally {HostApplicationServices.WorkingDatabase=previous;}
   return "CAD memory tests PASS: "+passed+". Active drawing was not read, modified, saved or closed.";
  }
  private static ObjectId MakeFixture(BlockTableRecord model,BlockTableRecord def,AttributeDefinition ad,string number,Transaction tr) {
   var br=new BlockReference(Point3d.Origin,def.ObjectId);var id=model.AppendEntity(br);tr.AddNewlyCreatedDBObject(br,true);
   var ar=new AttributeReference();ar.SetAttributeFromBlock(ad,br.BlockTransform);ar.TextString=number;br.AttributeCollection.AppendAttribute(ar);tr.AddNewlyCreatedDBObject(ar,true);
   WriteInstance(br,new Instance {Part=new Part {Model="TEST",Brand="TEST",Name="TEST"},Tag=number,Panel="TEST",DeviceNumberInputEnabled=true},tr);return id;
  }
  private static Instance FixtureInstance(Database db,ObjectId id) {
   using(var tr=db.TransactionManager.StartTransaction()){return ReadCurrentInstance((BlockReference)tr.GetObject(id,OpenMode.ForRead),tr);}
  }
 }
}
