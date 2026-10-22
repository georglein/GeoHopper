using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Edits a dedicated isotropic elastic PLAXIS plate material with explicit SI section stiffnesses.</summary>
    public sealed class PlateMaterialComponent : DefinitionComponent
    {
        /// <summary>Creates a section-level material editor independent of continuum soil materials.</summary>
        public PlateMaterialComponent():base("Plate Material","Plate Mat","Elastic isotropic plate stiffnesses per metre out of plane. Separate from continuum materials.","Materials") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique plate material ID",GH_ParamAccess.item,"WallPlateMaterial");
            p.AddNumberParameter("EA [N/m]","EA","Axial stiffness per metre out of plane",GH_ParamAccess.item,12e9);
            p.AddNumberParameter("EI [N m2/m]","EI","Bending stiffness per metre out of plane",GH_ParamAccess.item,160e6);
            p.AddNumberParameter("Poisson ratio","nu","Dimensionless; 0 to 0.499",GH_ParamAccess.item,.2);
            p.AddNumberParameter("Weight [N/m/m]","w","Weight per unit plate length per metre out of plane; not volume unit weight",GH_ParamAccess.item,9600);
            p.AddBooleanParameter("Prevent punching","Punching","Native PLAXIS prevent-punching option; inspect suitability for the model",GH_ParamAccess.item,true);
            p.AddColourParameter("Colour","Colour","Rhino line preview colour only",GH_ParamAccess.item,Color.FromArgb(130,140,150));
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Plate material","Material","Validated plate-specific material",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id=""; double ea=0,ei=0,nu=0,w=0; bool punching=true; Color colour=Color.Gray;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref ea)||!d.GetData(2,ref ei)||!d.GetData(3,ref nu)||
                    !d.GetData(4,ref w)||!d.GetData(5,ref punching)||!d.GetData(6,ref colour)) return;
                d.SetData(0,new DefinitionGoo<PlateMaterialData>(new PlateMaterialData(id,ea,ei,nu,w,punching,colour.ToArgb())));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed15"); } }
    }

    /// <summary>Defines a straight structural plate and previews its line using the material colour.</summary>
    public sealed class PlateComponent : DefinitionComponent
    {
        private readonly List<Line> lines=new List<Line>();
        private readonly List<Color> colours=new List<Color>();
        /// <summary>Creates a generic plate editor; no PLAXIS requests are sent here.</summary>
        public PlateComponent():base("Plate","Plate","Straight structural plate in World XY. Does not replace soil volume or create interfaces.","Structures") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique plate ID",GH_ParamAccess.item,"WallPlate");
            p.AddCurveParameter("Line","Line","One open straight line in World XY, Rhino document units",GH_ParamAccess.item);
            p.AddGenericParameter("Plate material","Material","Connect Plate Material, not a continuum Material",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Plate","Plate","Plate definition for Model",GH_ParamAccess.item);
            p.AddTextParameter("ID","ID","Use in Phase Active plates",GH_ParamAccess.item);
            p.AddNumberParameter("Length [m]","Length","Plate line length in metres",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void BeforeSolveInstance() { lines.Clear(); colours.Clear(); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id=""; Curve curve=null;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref curve)) return;
                var segment=AnalysisGeometry.Segment(curve);
                var material=Read<PlateMaterialData>(d,2);
                var plate=new PlateData(id,segment,material);
                lines.Add(new Line(curve.PointAtStart,curve.PointAtEnd)); colours.Add(Color.FromArgb(material.Argb));
                double dx=segment.B.X-segment.A.X,dy=segment.B.Y-segment.A.Y;
                d.SetData(0,new DefinitionGoo<PlateData>(plate)); d.SetData(1,plate.Id); d.SetData(2,Math.Sqrt(dx*dx+dy*dy));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override bool IsPreviewCapable { get { return true; } }
        /// <inheritdoc/>
        public override BoundingBox ClippingBox
        { get { var box=BoundingBox.Empty; foreach(var line in lines) box.Union(line.BoundingBox); return box; } }
        /// <inheritdoc/>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if(Hidden || Locked) return;
            for(int i=0;i<lines.Count;i++) args.Display.DrawLine(lines[i],Attributes.Selected?Color.LimeGreen:colours[i],3);
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed16"); } }
    }
}
