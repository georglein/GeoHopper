using System;
using System.Linq;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Headless analysis contract tests and native-protocol scenario driver.</summary>
internal static class AnalysisProbe
{
    /// <summary>Returns a rectangular contour for a small soil/wall test domain.</summary>
    private static XY[] Box(double x,double y,double w,double h)
    { return new[]{new XY(x,y),new XY(x+w,y),new XY(x+w,y+h),new XY(x,y+h)}; }
    /// <summary>Creates drained soil in canonical SI units.</summary>
    private static MaterialData Soil()
    { return new MaterialData("Soil",true,20e6,.3,18000,20000,5000,Math.PI/6,0,0); }
    /// <summary>Creates a two-region model with gravity, installation and surcharge phases.</summary>
    internal static ModelData Demo()
    {
        var soil=Soil();
        var wall=new MaterialData("Concrete",false,30e9,.2,24000,24000,0,0,0,0);
        return new ModelData("AnalysisDemo",new[]{new RegionData("Base",Box(0,-2,4,2),soil),
            new RegionData("Wall",Box(0,0,4,2),wall)},0,
            new[]{new InterfaceData("Contact",new SegmentData(new XY(0,0),new XY(4,0)),"Base","Wall",soil,true,.67)},
            new[]{new LoadData("Surcharge",new SegmentData(new XY(0,2),new XY(4,2)),"Wall",0,-10000)},
            new[]{new PhaseData("Initial","",new[]{"Base"},null,null,false),
                new PhaseData("Install","Initial",new[]{"Base","Wall"},new[]{"Contact"},null,true),
                new PhaseData("Load","Install",new[]{"Base","Wall"},new[]{"Contact"},new[]{"Surcharge"},false)},
            new MeshData(.5,new[]{"Wall"},1));
    }
    /// <summary>Asserts an invariant and produces a readable build log.</summary>
    private static void Check(string name,bool valid)
    { if(!valid) throw new Exception(name); Console.WriteLine("PASS: "+name); }
    /// <summary>Requires invalid analysis input to fail before transfer.</summary>
    private static void Reject(string name,Action action)
    { try { action(); } catch(ArgumentException) { Console.WriteLine("PASS: "+name); return; } throw new Exception("Expected rejection: "+name); }
    /// <summary>Tests topology, units, full activation states and parent ordering.</summary>
    private static void Core()
    {
        var model=Demo(); var a=model.Analysis;
        Check("Analysis phases preserve parent order",String.Join(",",a.Phases.Select(p=>p.Id))=="Initial,Install,Load");
        Check("Loads retain signed SI line forces",a.Loads[0].Command().Contains("\"qy_start\" -10000"));
        Check("Contact copy has manual strength and gap closure",a.Interfaces[0].MaterialCommand().Contains("\"InterfaceStrengthDetermination\" \"Manual\"") &&
            a.Interfaces[0].MaterialCommand().Contains("\"TensileStrength\" 0 \"GapClosure\" True"));
        Check("Contact does not mutate original soil",!Soil().Command().Contains("Rinter"));
        Check("Initial gravity displacement reset is explicit",!a.Phases[0].ResetDisplacements && a.Phases[1].ResetDisplacements && !a.Phases[2].ResetDisplacements);
        Check("Shared boundary recognized",a.Interfaces[0].Segment.OnBoundary(model.Regions[0]) && a.Interfaces[0].Segment.OnBoundary(model.Regions[1]));
        Reject("Zero-length line rejected",()=>new SegmentData(new XY(0,0),new XY(0,0)));
        Reject("Invalid contact factor rejected",()=>new InterfaceData("Bad",a.Interfaces[0].Segment,"Base","Wall",Soil(),true,0));
        Reject("Initial phase cannot reset",()=>new PhaseData("Initial","",new[]{"Base"},null,null,true));
        Reject("Initial phase cannot activate load",()=>new PhaseData("Initial","",new[]{"Base"},null,new[]{"Surcharge"},false));
        Reject("Duplicate active IDs rejected",()=>new PhaseData("X","Initial",new[]{"Base","base"},null,null,false));
        Reject("Analysis requires mesh",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases,null));
        Reject("Unknown refinement region rejected",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases,new MeshData(.5,new[]{"Unknown"},1)));
        Reject("Unknown load host rejected",()=>new AnalysisData(model.Regions,a.Interfaces,
            new[]{new LoadData("Surcharge",a.Loads[0].Segment,"Unknown",0,-1)},a.Phases,a.Mesh));
        Reject("Interior line cannot be a boundary load",()=>new AnalysisData(model.Regions,a.Interfaces,
            new[]{new LoadData("Surcharge",new SegmentData(new XY(1,1),new XY(3,1)),"Wall",0,-1)},a.Phases,a.Mesh));
        Reject("Contact requires a shared boundary",()=>new AnalysisData(model.Regions,
            new[]{new InterfaceData("Contact",a.Loads[0].Segment,"Base","Wall",Soil(),true,.67)},a.Loads,a.Phases,a.Mesh));
        Reject("Contact cannot activate without both regions",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("Install","Initial",new[]{"Base"},new[]{"Contact"},null,false)},a.Mesh));
        Reject("Load cannot activate on inactive host",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("Install","Initial",new[]{"Base"},null,new[]{"Surcharge"},false)},a.Mesh));
        Reject("Cyclic phases rejected",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("A","B",new[]{"Base"},null,null,false),new PhaseData("B","A",new[]{"Base"},null,null,false)},a.Mesh));
        var reversed=new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases.Reverse(),a.Mesh);
        Check("Unordered phase inputs sorted",reversed.Phases[0].Id=="Initial" && reversed.Phases[2].Id=="Load");
        Reject("Analysis with contour margin rejected",()=>new ModelData(model.Id,model.Regions,1,a.Interfaces,a.Loads,a.Phases,a.Mesh));
        var changed=new ModelData(model.Id,model.Regions,0,a.Interfaces,a.Loads,a.Phases,new MeshData(.25,null,0));
        Check("Mesh changes invalidate model fingerprint",model.Fingerprint!=changed.Fingerprint);
        Reject("Overlapping contacts rejected",()=>new AnalysisData(model.Regions,new[]{a.Interfaces[0],
            new InterfaceData("Other",a.Interfaces[0].Segment,"Base","Wall",Soil(),false,.67)},a.Loads,a.Phases,a.Mesh));
    }

    /// <summary>Synthetic plate fixture with installation and subsequent deactivation; not an engineering demo.</summary>
    internal static ModelData PlateDemo()
    {
        var m=new PlateMaterialData("PlateConcrete",12e9,160e6,.2,9600,true,0);
        var plates=new[]{new PlateData("Left",new SegmentData(new XY(1,-1),new XY(1,1)),m),
            new PlateData("Right",new SegmentData(new XY(3,-1),new XY(3,1)),m)};
        return new ModelData("AnalysisDemo",Demo().Regions,0,null,null,
            new[]{new PhaseData("Initial","",new[]{"Base","Wall"},null,null,false),
                new PhaseData("Install","Initial",new[]{"Base","Wall"},null,null,true,new[]{"Left","Right"}),
                new PhaseData("Remove","Install",new[]{"Base","Wall"},null,null,false,new[]{"Right"})},
            new MeshData(.5,null,0),plates);
    }
    /// <summary>Checks plate invariants, compatibility and stale-result invalidation before any HTTP calls.</summary>
    private static void PlateCore()
    {
        var model=PlateDemo(); var plate=model.Plates[0]; var material=plate.Material;
        Check("Plate creation uses native plate command",plate.Command()=="plate (1 -1) (1 1)");
        Check("Plate material uses documented plate properties",material.Command().Contains("\"Isotropic\" True") && material.Command().Contains("\"StructNu\"") && !material.Command().Contains("Eref"));
        Check("Plate installation and removal are full snapshots",model.Analysis.Phases[0].Plates.Count==0 && model.Analysis.Phases[1].Plates.Count==2 && model.Analysis.Phases[2].Plates.Count==1);
        Check("Legacy models have no plates",Demo().Plates.Count==0 && Demo().Analysis.Phases.All(p=>p.Plates.Count==0));
        Reject("Plate rejects continuum material absence",()=>new PlateData("X",plate.Segment,null));
        Reject("Plate rejects zero EA",()=>new PlateMaterialData("X",0,1,.2,1,true,0));
        Reject("Plate rejects non-finite EI",()=>new PlateMaterialData("X",1,Double.NaN,.2,1,true,0));
        Reject("Plate rejects negative weight",()=>new PlateMaterialData("X",1,1,.2,-1,true,0));
        Reject("Plate rejects invalid Poisson ratio",()=>new PlateMaterialData("X",1,1,.5,1,true,0));
        Reject("Initial phase cannot activate plates",()=>new PhaseData("Initial","",new[]{"Base"},null,null,false,new[]{"Left"}));
        Reject("Unknown active plate rejected",()=>new ModelData("X",model.Regions,0,null,null,model.Analysis.Phases,model.Analysis.Mesh));
        Reject("Duplicate plate IDs rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,plate}));
        Reject("Overlapping plate geometry rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,new PlateData("Other",plate.Segment,material)}));
        Reject("Out-of-domain plate rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{new PlateData("Other",new SegmentData(new XY(1,0),new XY(5,0)),material)}));
        Reject("Conflicting shared plate materials rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,new PlateData("Other",model.Plates[1].Segment,new PlateMaterialData(material.Id,1,1,.2,1,true,0))}));
        Reject("Continuum and plate material ID collision rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{new PlateData("Other",plate.Segment,new PlateMaterialData("Soil",1,1,.2,1,true,0))}));
        var geometry=new ModelData("X",model.Regions,0,plates:model.Plates);
        Check("Geometry-only plates do not force analysis",geometry.Analysis==null && geometry.Plates.Count==2);
        var changed=new ModelData("X",model.Regions,0,plates:new[]{new PlateData(plate.Id,plate.Segment,new PlateMaterialData("Changed",material.EA*2,material.EI,.2,9600,true,0)),model.Plates[1]});
        Check("Plate stiffness changes invalidate fingerprint",geometry.Fingerprint!=changed.Fingerprint);
        var phaseChanged=new PhaseData("Remove","Install",new[]{"Base","Wall"},null,null,false,new[]{"Left"});
        Check("Plate activation changes invalidate phase signature",model.Analysis.Phases[2].Signature()!=phaseChanged.Signature());
    }


    /// <summary>Constructs a dry four-region excavation with two plate walls and segmented face contacts.</summary>
    internal static ModelData ExcavationDemo()
    {
        var soil=Soil();
        var regions=new[]{new RegionData("LeftSoil",Box(-3,-6,2,6),soil),new RegionData("DeepSoil",Box(-1,-6,2,5),soil),
            new RegionData("Cut",Box(-1,-1,2,1),soil),new RegionData("RightSoil",Box(1,-6,2,6),soil)};
        var mat=new PlateMaterialData("PlateConcrete",12e9,160e6,.2,9600,true,0);
        var plates=new[]{new PlateData("Left",new SegmentData(new XY(-1,-4),new XY(-1,0)),mat),
            new PlateData("Right",new SegmentData(new XY(1,-4),new XY(1,0)),mat)};
        var contacts=new System.Collections.Generic.List<InterfaceData>();
        foreach(var p in plates)
        {
            bool left=p.Id=="Left"; double x=p.Segment.A.X;
            var bottom=new SegmentData(new XY(x,-4),new XY(x,-1));
            var top=new SegmentData(new XY(x,-1),new XY(x,0));
            contacts.Add(new InterfaceData(p.Id+"OuterBottom",bottom,p,left?"LeftSoil":"RightSoil",soil,true,.67));
            contacts.Add(new InterfaceData(p.Id+"OuterTop",top,p,left?"LeftSoil":"RightSoil",soil,true,.67));
            contacts.Add(new InterfaceData(p.Id+"InnerBottom",bottom,p,"DeepSoil",soil,false,.67));
            contacts.Add(new InterfaceData(p.Id+"InnerTop",top,p,"Cut",soil,false,.67));
        }
        var all=regions.Select(p=>p.Id).ToArray(); var activePlates=plates.Select(p=>p.Id).ToArray();
        var phases=new[]{new PhaseData("Initial","",all,null,null,false,null,"K0 procedure"),
            new PhaseData("Install","Initial",all,contacts.Select(c=>c.Id),null,true,activePlates),
            new PhaseData("Excavate","Install",all.Where(id=>id!="Cut"),contacts.Where(c=>c.RegionA!="Cut").Select(c=>c.Id),null,false,activePlates)};
        return new ModelData("AnalysisDemo",regions,0,contacts,null,phases,new MeshData(.5,null,1,activePlates),plates);
    }
    /// <summary>Exercises contact topology, excavation snapshots, mesh references and conservative K0 eligibility.</summary>
    private static void ExcavationCore()
    {
        var model=ExcavationDemo(); var a=model.Analysis; var p=model.Plates[0]; var c=a.Interfaces[0];
        Check("K0 excavation fixture accepted",a.Phases[0].InitialMethod=="K0 procedure" && a.Interfaces.Count==8);
        Check("Excavation preserves outer contacts and removes two inner contacts",a.Phases[2].Interfaces.Count==6 && !a.Phases[2].Regions.Contains("Cut"));
        Check("Opposite plate faces can coincide",a.Interfaces[0].OppositeFace(a.Interfaces[2]));
        var reversed=new InterfaceData("Reversed",new SegmentData(c.Segment.B,c.Segment.A),p,c.RegionA,Soil(),true,.67);
        Check("Contact direction normalized to source plate",reversed.Segment.Command()==c.Segment.Command());
        Reject("Off-plate contact rejected",()=>new InterfaceData("Bad",new SegmentData(new XY(0,-2),new XY(0,-1)),p,"Cut",Soil(),true,.67));
        Reject("Unknown plate refinement rejected",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases,new MeshData(.5,null,1,new[]{"Missing"}),model.Plates));
        Reject("Unknown initial method rejected",()=>new PhaseData("Initial","",new[]{"LeftSoil"},null,null,false,null,"K0"));
        Reject("K0 selection rejected on child",()=>new PhaseData("Child","Initial",new[]{"LeftSoil"},null,null,false,null,"K0 procedure"));
        var noPlate=new PhaseData("Install","Initial",a.Phases[1].Regions,a.Phases[1].Interfaces,null,true,new[]{"Right"});
        Reject("Active plate contact needs active plate",()=>new AnalysisData(model.Regions,a.Interfaces,null,new[]{a.Phases[0],noPlate},a.Mesh,model.Plates));
        var cutContact=new PhaseData("Excavate","Install",a.Phases[2].Regions,a.Phases[1].Interfaces,null,false,a.Phases[2].Plates);
        Reject("Excavated soil contact cannot remain active",()=>new AnalysisData(model.Regions,a.Interfaces,null,new[]{a.Phases[0],a.Phases[1],cutContact},a.Mesh,model.Plates));
        var duplicate=new InterfaceData("Duplicate",c.Segment,p,c.RegionA,Soil(),true,.67);
        Reject("Same-face overlap rejected",()=>new AnalysisData(model.Regions,a.Interfaces.Concat(new[]{duplicate}),null,a.Phases,a.Mesh,model.Plates));
        var partial=new InterfaceData("Partial",new SegmentData(new XY(-1,-3),new XY(-1,-2)),p,"DeepSoil",Soil(),false,.67);
        Reject("Opposite faces require matching segment boundaries",()=>new AnalysisData(model.Regions,new[]{c,partial},null,new[]{a.Phases[0],new PhaseData("Install","Initial",a.Phases[0].Regions,null,null,false)},a.Mesh,model.Plates));
        var changedPlate=new PlateData(p.Id,p.Segment,new PlateMaterialData("Changed",1e9,1e7,.2,1000,true,0));
        Reject("Stale contact plate definition rejected",()=>new AnalysisData(model.Regions,a.Interfaces,null,a.Phases,a.Mesh,new[]{changedPlate,model.Plates[1]}));
        var wrongBoundary=new InterfaceData("Bad",c.Segment,p,"Cut",Soil(),true,.67);
        Reject("Contact must be on named soil boundary",()=>new AnalysisData(model.Regions,new[]{wrongBoundary},null,a.Phases,a.Mesh,model.Plates));
        var init=new PhaseData("Initial","",model.Regions.Select(x=>x.Id).Where(x=>x!="Cut"),null,null,false,null,"K0 procedure");
        Reject("K0 initial excavation rejected",()=>InitialStateRules.ValidateK0(model.Regions,init));
        var wallDemo=Demo();
        Reject("K0 continuum concrete rejected",()=>InitialStateRules.ValidateK0(wallDemo.Regions,new PhaseData("Initial","",wallDemo.Regions.Select(x=>x.Id),null,null,false,null,"K0 procedure")));
        var slope=new[]{new RegionData("Slope",new[]{new XY(0,0),new XY(2,0),new XY(2,1),new XY(0,2)},Soil())};
        Reject("K0 slope rejected",()=>InitialStateRules.ValidateK0(slope,new PhaseData("Initial","",new[]{"Slope"},null,null,false,null,"K0 procedure")));
        var gap=new[]{new RegionData("A",Box(0,0,1,1),Soil()),new RegionData("B",Box(2,0,1,1),Soil())};
        Reject("K0 gaps rejected",()=>InitialStateRules.ValidateK0(gap,new PhaseData("Initial","",new[]{"A","B"},null,null,false,null,"K0 procedure")));
        var other=new MaterialData("Other",true,40e6,.3,18000,20000,5000,Math.PI/6,0,0);
        var lateral=new[]{new RegionData("A",Box(0,0,1,1),Soil()),new RegionData("B",Box(1,0,1,1),other)};
        Reject("K0 lateral material discontinuity rejected",()=>InitialStateRules.ValidateK0(lateral,new PhaseData("Initial","",new[]{"A","B"},null,null,false,null,"K0 procedure")));
        var layered=new[]{new RegionData("A",Box(0,0,2,1),Soil()),new RegionData("B",Box(0,1,2,1),other)};
        InitialStateRules.ValidateK0(layered,new PhaseData("Initial","",new[]{"A","B"},null,null,false,null,"K0 procedure"));
        Check("K0 horizontal layers accepted",true);
        Check("Initial method changes invalidate phase signature",a.Phases[0].Signature()!=new PhaseData("Initial","",a.Phases[0].Regions,null,null,false).Signature());
        Check("Plate refinement changes invalidate mesh signature",a.Mesh.Signature()!=new MeshData(.5,null,1).Signature());
        Check("Legacy defaults remain gravity and no plate refinement",Demo().Analysis.Phases[0].InitialMethod=="Gravity loading" && Demo().Analysis.Mesh.RefinePlates.Count==0);
    }


    /// <summary>Synthetic five-region excavation with two struts, installation, deeper excavation and removal.</summary>
    internal static ModelData StrutDemo()
    {
        var soil=Soil();
        var regions=new[]{new RegionData("LeftSoil",Box(-3,-6,2,6),soil),new RegionData("DeepSoil",Box(-1,-6,2,4),soil),
            new RegionData("Cut2",Box(-1,-2,2,1),soil),new RegionData("Cut",Box(-1,-1,2,1),soil),new RegionData("RightSoil",Box(1,-6,2,6),soil)};
        var plates=ExcavationDemo().Plates;
        var contacts=new System.Collections.Generic.List<InterfaceData>();
        foreach(var p in plates)
        {
            var levels=new[]{-4.0,-2.0,-1.0,0.0}; var hosts=new[]{"DeepSoil","Cut2","Cut"};
            for(int i=0;i<3;i++)
            {
                var segment=new SegmentData(new XY(p.Segment.A.X,levels[i]),new XY(p.Segment.A.X,levels[i+1]));
                contacts.Add(new InterfaceData(p.Id+"Outer"+i,segment,p,p.Id=="Left"?"LeftSoil":"RightSoil",soil,true,.67));
                contacts.Add(new InterfaceData(p.Id+"Inner"+i,segment,p,hosts[i],soil,false,.67));
            }
        }
        var material=new AnchorMaterialData("StrutSteel",2.1e9,3,0);
        var anchors=new[]{new AnchorData("Strut1",new SegmentData(new XY(-1,-.5),new XY(1,-.5)),material,"Left","Right"),
            new AnchorData("Strut2",new SegmentData(new XY(-1,-.75),new XY(1,-.75)),material,"Left","Right")};
        var all=regions.Select(p=>p.Id).ToArray(); var plateIds=plates.Select(p=>p.Id).ToArray();
        var afterFirst=all.Where(id=>id!="Cut").ToArray(); var afterSecond=afterFirst.Where(id=>id!="Cut2").ToArray();
        var firstContacts=contacts.Where(c=>c.RegionA!="Cut").Select(c=>c.Id).ToArray();
        var secondContacts=contacts.Where(c=>c.RegionA!="Cut" && c.RegionA!="Cut2").Select(c=>c.Id).ToArray();
        var anchorIds=anchors.Select(a=>a.Id).ToArray();
        var phases=new[]{new PhaseData("Initial","",all,null,null,false,null,"K0 procedure"),
            new PhaseData("Install","Initial",all,contacts.Select(c=>c.Id),null,true,plateIds),
            new PhaseData("Excavate","Install",afterFirst,firstContacts,null,false,plateIds),
            new PhaseData("Struts","Excavate",afterFirst,firstContacts,null,false,plateIds,"Gravity loading",anchorIds),
            new PhaseData("Deepen","Struts",afterSecond,secondContacts,null,false,plateIds,"Gravity loading",anchorIds),
            new PhaseData("Remove","Deepen",afterSecond,secondContacts,null,false,plateIds,"Gravity loading",new[]{"Strut2"})};
        return new ModelData("AnalysisDemo",regions,0,contacts,null,phases,new MeshData(.5,null,1,plateIds),plates,anchors);
    }
    /// <summary>Checks discrete-anchor units, support references, identity and activation constraints.</summary>
    private static void AnchorCore()
    {
        var model=StrutDemo(); var a=model.Anchors[0]; var m=a.Material;
        Check("Anchor EA is per element and spacing is separate",m.Command().Contains("\"EA\" 2100000000") && m.Command().Contains("\"LSpacing\" 3"));
        Check("Anchor uses native n2n command",a.Command().StartsWith("n2nanchor "));
        Check("Strut installation is later than first excavation",model.Analysis.Phases[2].Anchors.Count==0 && model.Analysis.Phases[3].Anchors.Count==2);
        Check("Deeper excavation preserves struts and removes cut",model.Analysis.Phases[4].Anchors.Count==2 && !model.Analysis.Phases[4].Regions.Contains("Cut2"));
        Check("Legacy models have no anchors",Demo().Anchors.Count==0 && Demo().Analysis.Phases.All(p=>p.Anchors.Count==0));
        Reject("Zero anchor EA rejected",()=>new AnchorMaterialData("X",0,1,0));
        Reject("Nonfinite anchor EA rejected",()=>new AnchorMaterialData("X",Double.NaN,1,0));
        Reject("Zero spacing rejected",()=>new AnchorMaterialData("X",1,0,0));
        Reject("Infinite spacing rejected",()=>new AnchorMaterialData("X",1,Double.PositiveInfinity,0));
        Reject("Missing anchor material rejected",()=>new AnchorData("X",a.Segment,null));
        Reject("Missing anchor segment rejected",()=>new AnchorData("X",null,m));
        Reject("Initial anchors rejected",()=>new PhaseData("Initial","",new[]{"LeftSoil"},null,null,false,null,"Gravity loading",new[]{"Strut1"}));
        Reject("Duplicate active anchor IDs rejected",()=>new PhaseData("X","Initial",new[]{"LeftSoil"},null,null,false,null,"Gravity loading",new[]{"Strut1","strut1"}));
        Reject("Duplicate anchor IDs rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{a,a}));
        Reject("Reversed overlapping anchor rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{a,new AnchorData("Other",new SegmentData(a.Segment.B,a.Segment.A),m)}));
        Reject("Unknown attachment plate rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{new AnchorData("X",a.Segment,m,"Unknown","Right")}));
        Reject("Endpoint off named plate rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{new AnchorData("X",a.Segment,m,"Right","Left")}));
        Reject("Anchor outside contour rejected",()=>new ModelData("X",model.Regions,0,anchors:new[]{new AnchorData("X",new SegmentData(new XY(-4,0),new XY(1,0)),m)}));
        Reject("Conflicting anchor material definitions rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{a,new AnchorData("X",model.Anchors[1].Segment,new AnchorMaterialData(m.Id,m.EA,4,0))}));
        Reject("Anchor and soil material ID collision rejected",()=>new ModelData("X",model.Regions,0,anchors:new[]{new AnchorData("X",a.Segment,new AnchorMaterialData("Soil",1,1,0))}));
        Reject("Anchor and plate material ID collision rejected",()=>new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{new AnchorData("X",a.Segment,new AnchorMaterialData(model.Plates[0].Material.Id,1,1,0))}));
        var phases=model.Analysis.Phases.ToArray();
        phases[3]=new PhaseData("Struts","Excavate",phases[3].Regions,phases[3].Interfaces,null,false,phases[3].Plates,"Gravity loading",new[]{"Missing"});
        Reject("Unknown active anchor rejected",()=>new AnalysisData(model.Regions,model.Analysis.Interfaces,null,phases,model.Analysis.Mesh,model.Plates,model.Anchors));
        var inactiveSupport=new PhaseData("X","Initial",model.Analysis.Phases[0].Regions,null,null,false,new[]{"Left"});
        Reject("Active anchor requires declared supporting plates",()=>a.ValidatePhase(inactiveSupport));
        var unbound=new ModelData("X",model.Regions,0,anchors:new[]{new AnchorData("X",a.Segment,m)});
        Check("Generic geometry-only anchor may omit plate references",unbound.Anchors.Count==1 && unbound.Analysis==null);
        var changed=new ModelData("X",model.Regions,0,anchors:new[]{new AnchorData("X",a.Segment,new AnchorMaterialData(m.Id,m.EA,4,0))});
        Check("Spacing changes invalidate model fingerprint",unbound.Fingerprint!=changed.Fingerprint);
        Check("Anchor activation changes invalidate phase signature",model.Analysis.Phases[3].Signature()!=new PhaseData("Struts","Excavate",model.Analysis.Phases[3].Regions,model.Analysis.Phases[3].Interfaces,null,false,model.Analysis.Phases[3].Plates).Signature());
        var attached=new ModelData("X",model.Regions,0,plates:model.Plates,anchors:new[]{a});
        Check("Optional support references participate in signatures",a.Signature()!=new AnchorData(a.Id,a.Segment,m).Signature() && attached.Anchors.Count==1);
    }

    /// <summary>Runs core assertions or a build/solve sequence against an isolated server.</summary>
    public static int Main(string[] args)
    {
        if(args.Length==0) { Core(); PlateCore(); ExcavationCore(); AnchorCore(); return 0; }
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var connection=new ConnectionData("127.0.0.1",Int32.Parse(args[0]),Console.ReadLine());
        var build=ModelBuilder.BuildAsync(args[2]=="struts"?StrutDemo():args[2]=="excavation"?ExcavationDemo():args[2]=="plates"?PlateDemo():Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
        CalculationResult calc=null;
        if(build.Success && build.Receipt!=null)
        {
            if(args[2]=="stale")
                ModelBuilder.BuildAsync(Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
            calc=ModelBuilder.CalculateAsync(build.Receipt,connection,1,args[2]=="output",s=>{}).GetAwaiter().GetResult();
        }
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{
            BuildSuccess=build.Success,BuildError=build.Error,HasReceipt=build.Receipt!=null,
            Success=calc!=null&&calc.Success,Error=calc==null?"":calc.Error,
            Status=calc==null?new string[0]:calc.PhaseStatus.ToArray()
        }));
        return 0;
    }
}
