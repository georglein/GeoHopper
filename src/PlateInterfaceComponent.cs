using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Defines one soil-facing segment of a plate contact; shares the existing Interface output contract.</summary>
    public sealed class PlateInterfaceComponent : DefinitionComponent
    {
        /// <summary>Creates an explicit plate/soil editor without changing the legacy two-region Interface component.</summary>
        public PlateInterfaceComponent():base("Plate Interface","Plate Contact","One plate/soil contact segment. Split at excavation boundaries and inspect native positive/negative side markers.","Geometry") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique interface ID across all interface types",GH_ParamAccess.item,"PlateContact");
            p.AddGenericParameter("Plate","Plate","Source Plate definition, also connected to Model",GH_ParamAccess.item);
            p.AddCurveParameter("Segment","Segment","Straight contact segment on the plate and one soil region boundary; Rhino units",GH_ParamAccess.item);
            p.AddTextParameter("Soil region","Region","Adjacent soil region ID; split segments at soil/excavation boundaries",GH_ParamAccess.item);
            p.AddGenericParameter("Soil material","Material","Drained Mohr-Coulomb source for the contact material",GH_ParamAccess.item);
            p.AddBooleanParameter("Positive side","Positive","Native positive/negative face; verify its marker lies in the named soil in PLAXIS",GH_ParamAccess.item,true);
            p.AddNumberParameter("Rinter","Rinter","Interface strength reduction, 0.01 to 1",GH_ParamAccess.item,.67);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Interface","Interface","Connect to Model.Interfaces together with other interfaces",GH_ParamAccess.item);
            p.AddTextParameter("ID","ID","Use in Phase.Active interfaces while both soil and plate are active",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id="",region=""; Curve line=null; bool positive=true; double factor=.67;
                if(!d.GetData(0,ref id)||!d.GetData(2,ref line)||!d.GetData(3,ref region)||!d.GetData(5,ref positive)||!d.GetData(6,ref factor)) return;
                var contact=new InterfaceData(id,AnalysisGeometry.Segment(line),Read<PlateData>(d,1),region,Read<MaterialData>(d,4),positive,factor);
                d.SetData(0,new DefinitionGoo<InterfaceData>(contact)); d.SetData(1,contact.Id);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed17"); } }
    }
}
