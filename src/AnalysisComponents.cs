using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Shared conversion for straight load/contact curves in the active Rhino document.</summary>
    internal static class AnalysisGeometry
    {
        /// <summary>Converts an open straight XY curve to canonical metres, rejecting implicit projections.</summary>
        internal static SegmentData Segment(Curve curve)
        {
            var doc=RhinoDoc.ActiveDoc;
            if(doc==null || doc.ModelUnitSystem==UnitSystem.None || doc.ModelUnitSystem==UnitSystem.CustomUnits)
                throw new ArgumentException("Set a supported Rhino document unit system.");
            double scale=RhinoMath.UnitScale(doc.ModelUnitSystem,UnitSystem.Meters);
            Rules.Range(scale,1e-12,1e12,"Rhino-to-metre scale");
            if(curve==null || curve.IsClosed || !curve.IsLinear(Rules.Tolerance/scale))
                throw new ArgumentException("Provide one open straight line in World XY.");
            var a=curve.PointAtStart; var b=curve.PointAtEnd;
            Rules.Range(a.Z*scale,-Rules.Tolerance,Rules.Tolerance,"Start Z [m]");
            Rules.Range(b.Z*scale,-Rules.Tolerance,Rules.Tolerance,"End Z [m]");
            return new SegmentData(new XY(a.X*scale,a.Y*scale),new XY(b.X*scale,b.Y*scale));
        }
    }

    /// <summary>General soil-structure contact definition with explicit geometry, side and source material.</summary>
    public sealed class InterfaceComponent : DefinitionComponent
    {
        /// <summary>Creates an interface editor; it performs no PLAXIS writes.</summary>
        public InterfaceComponent():base("Interface","Interface","Contact on a shared straight boundary. Check positive/negative side in PLAXIS before calculation.","Geometry") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique contact ID",GH_ParamAccess.item,"Contact");
            p.AddCurveParameter("Line","Line","Directed straight line in World XY, Rhino document units",GH_ParamAccess.item);
            p.AddTextParameter("Region A","A","First adjacent region ID",GH_ParamAccess.item,"Wall");
            p.AddTextParameter("Region B","B","Second adjacent region ID",GH_ParamAccess.item,"BaseSoil");
            p.AddGenericParameter("Soil material","Material","Drained Mohr-Coulomb source for a separate contact material",GH_ParamAccess.item);
            p.AddBooleanParameter("Positive side","Positive","True: positive interface; False: negative. Inspect side markers in PLAXIS.",GH_ParamAccess.item,true);
            p.AddNumberParameter("Rinter","Rinter","Interface strength reduction factor, 0.01 to 1; demonstration default only",GH_ParamAccess.item,.67);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddGenericParameter("Interface","Interface","Contact definition",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id="",a="",b=""; Curve line=null; bool positive=true; double r=.67;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref line)||!d.GetData(2,ref a)||!d.GetData(3,ref b)||
                    !d.GetData(5,ref positive)||!d.GetData(6,ref r)) return;
                d.SetData(0,new DefinitionGoo<InterfaceData>(new InterfaceData(id,AnalysisGeometry.Segment(line),a,b,Read<MaterialData>(d,4),positive,r)));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed06"); } }
    }

    /// <summary>Uniform global line force on a named region boundary.</summary>
    public sealed class LineLoadComponent : DefinitionComponent
    {
        /// <summary>Creates an SI load editor; negative qy points down.</summary>
        public LineLoadComponent():base("Line Load","Line Load","Uniform line force [N/m] on a one-metre out-of-plane strip. Negative qy acts downwards.","Loads") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique load ID",GH_ParamAccess.item,"Surcharge");
            p.AddCurveParameter("Line","Line","Straight boundary line in World XY, Rhino document units",GH_ParamAccess.item);
            p.AddTextParameter("Region","Region","Host region ID",GH_ParamAccess.item,"Backfill");
            p.AddNumberParameter("qx [N/m]","qx","Global horizontal line force per metre; one-metre out-of-plane strip",GH_ParamAccess.item,0);
            p.AddNumberParameter("qy [N/m]","qy","Global vertical line force per metre; negative is downward",GH_ParamAccess.item,-10000);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddGenericParameter("Load","Load","Uniform load definition",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id="",region=""; Curve line=null; double x=0,y=0;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref line)||!d.GetData(2,ref region)||!d.GetData(3,ref x)||!d.GetData(4,ref y)) return;
                d.SetData(0,new DefinitionGoo<LoadData>(new LoadData(id,AnalysisGeometry.Segment(line),region,x,y)));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed07"); } }
    }

    /// <summary>Defines one complete dry activation state and its parent phase.</summary>
    public sealed class PhaseComponent : DefinitionComponent
    {
        /// <summary>Creates a phase editor for initial gravity loading or subsequent plastic stages.</summary>
        public PhaseComponent():base("Phase","Phase","Full active-ID lists. Empty Parent creates the selected initial phase; other phases are Plastic. Dry conditions.","Analysis") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique phase ID",GH_ParamAccess.item,"Initial");
            p.AddTextParameter("Parent","Parent","Parent phase ID; empty means initial phase",GH_ParamAccess.item,""); p[1].Optional=true;
            p.AddTextParameter("Active regions","Regions","Full list of active region IDs, not only changes",GH_ParamAccess.list);
            p.AddTextParameter("Active interfaces","Interfaces","Full list of active contact IDs",GH_ParamAccess.list); p[3].Optional=true;
            p.AddTextParameter("Active loads","Loads","Full list of active load IDs",GH_ParamAccess.list); p[4].Optional=true;
            p.AddBooleanParameter("Reset displacements","Reset","Reset at this phase start; normally true only just after gravity initialization",GH_ParamAccess.item,false);
            p.AddTextParameter("Active plates","Plates","Full list of active plate IDs; empty means all plates inactive",GH_ParamAccess.list); p[6].Optional=true;
            p.AddTextParameter("Initial method","Initial","Initial phase only: Gravity loading or K0 procedure. K0 supports dry horizontal soil layers.",GH_ParamAccess.item,"Gravity loading");
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Phase","Phase","Dry phase definition",GH_ParamAccess.item); p.AddTextParameter("ID","ID","Use as the Parent of a later phase",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id="",parent=""; bool reset=false; var regions=new List<string>(); var contacts=new List<string>(); var loads=new List<string>();
                if(!d.GetData(0,ref id)||!d.GetDataList(2,regions)||!d.GetData(5,ref reset)) return;
                d.GetData(1,ref parent); d.GetDataList(3,contacts); d.GetDataList(4,loads);
                var plates=new List<string>(); d.GetDataList(6,plates);
                string method="Gravity loading"; d.GetData(7,ref method);
                var phase=new PhaseData(id,parent,regions,contacts,loads,reset,plates,method);
                d.SetData(0,new DefinitionGoo<PhaseData>(phase)); d.SetData(1,phase.Id);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed08"); } }
    }

    /// <summary>Global mesh dimension and local polygon refinements.</summary>
    public sealed class MeshSettingsComponent : DefinitionComponent
    {
        /// <summary>Creates mesh settings using metres regardless of Rhino document units.</summary>
        public MeshSettingsComponent():base("Mesh Settings","Mesh","Absolute element size [m], with optional local region and plate-line refinement.","Analysis") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Element size [m]","Size","Global target element dimension in metres",GH_ParamAccess.item,.5);
            p.AddTextParameter("Refine regions","Regions","Optional region IDs to refine locally",GH_ParamAccess.list); p[1].Optional=true;
            p.AddIntegerParameter("Refinement passes","Passes","Number of local refine operations, 0 to 3",GH_ParamAccess.item,1);
            p.AddTextParameter("Refine plates","Plates","Optional plate IDs for local line refinement",GH_ParamAccess.list); p[3].Optional=true;
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddGenericParameter("Mesh","Mesh","Mesh settings definition",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                double size=0; int passes=0; var ids=new List<string>();
                if(!d.GetData(0,ref size)||!d.GetData(2,ref passes)) return;
                var plates=new List<string>(); d.GetDataList(3,plates);
                d.GetDataList(1,ids); d.SetData(0,new DefinitionGoo<MeshData>(new MeshData(size,ids,passes,plates)));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed09"); } }
    }
}
