using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Edits elastic axial stiffness and out-of-plane spacing for discrete anchors or struts.</summary>
    public sealed class AnchorMaterialComponent : DefinitionComponent
    {
        /// <summary>Creates an anchor material editor; EA is per element, not per metre of wall.</summary>
        public AnchorMaterialComponent():base("Anchor Material","Anchor Mat","Elastic axial material: EA [N] per individual anchor and spacing [m]. No buckling or capacity check.","Materials") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique anchor material ID",GH_ParamAccess.item,"StrutMaterial");
            p.AddNumberParameter("EA [N]","EA","Axial rigidity per individual element; do not divide by spacing",GH_ParamAccess.item,2.1e9);
            p.AddNumberParameter("Spacing [m]","Spacing","Distance between elements out of plane; independent of Rhino units",GH_ParamAccess.item,3);
            p.AddColourParameter("Colour","Colour","Rhino preview colour only",GH_ParamAccess.item,Color.FromArgb(90,120,145));
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Anchor material","Material","Elastic AnchorMat definition",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id=""; double ea=0,spacing=0; Color colour=Color.Gray;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref ea)||!d.GetData(2,ref spacing)||!d.GetData(3,ref colour)) return;
                d.SetData(0,new DefinitionGoo<AnchorMaterialData>(new AnchorMaterialData(id,ea,spacing,colour.ToArgb())));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed18"); } }
    }

    /// <summary>Defines a straight axial connection and previews its reference line in Rhino document units.</summary>
    public sealed class NodeToNodeAnchorComponent : DefinitionComponent
    {
        private readonly List<Line> lines=new List<Line>();
        private readonly List<Color> colours=new List<Color>();
        /// <summary>Creates a generic unprestressed node-to-node anchor; optional host IDs add validation.</summary>
        public NodeToNodeAnchorComponent():base("Node-to-Node Anchor","N2N Anchor","Straight elastic axial element. Optional plate IDs validate endpoint coincidence and active supports. No prestress or bending.","Structures") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique anchor ID",GH_ParamAccess.item,"Strut1");
            p.AddCurveParameter("Line","Line","One open straight line in World XY; endpoints define the connection",GH_ParamAccess.item);
            p.AddGenericParameter("Anchor material","Material","Connect Anchor Material, not Plate Material",GH_ParamAccess.item);
            p.AddTextParameter("Start plate","Start","Optional plate ID at Line start; empty disables this attachment check",GH_ParamAccess.item,""); p[3].Optional=true;
            p.AddTextParameter("End plate","End","Optional plate ID at Line end; empty disables this attachment check",GH_ParamAccess.item,""); p[4].Optional=true;
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Anchor","Anchor","Connect to Model.Anchors",GH_ParamAccess.item);
            p.AddTextParameter("ID","ID","Use in Phase.Active anchors",GH_ParamAccess.item);
            p.AddNumberParameter("Length [m]","Length","Reference length in metres",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void BeforeSolveInstance() { lines.Clear(); colours.Clear(); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try {
                string id="",start="",end=""; Curve curve=null;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref curve)) return;
                d.GetData(3,ref start); d.GetData(4,ref end);
                var segment=AnalysisGeometry.Segment(curve); var material=Read<AnchorMaterialData>(d,2);
                var anchor=new AnchorData(id,segment,material,start,end);
                lines.Add(new Line(curve.PointAtStart,curve.PointAtEnd)); colours.Add(Color.FromArgb(material.Argb));
                double dx=segment.B.X-segment.A.X,dy=segment.B.Y-segment.A.Y;
                d.SetData(0,new DefinitionGoo<AnchorData>(anchor)); d.SetData(1,anchor.Id); d.SetData(2,Math.Sqrt(dx*dx+dy*dy));
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
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed19"); } }
    }
}
