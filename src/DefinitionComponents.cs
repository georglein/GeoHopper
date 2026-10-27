using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Transient GH carrier for immutable definitions; no connection secrets are serialized.</summary>
    internal sealed class DefinitionGoo<T> : GH_Goo<T> where T : class
    {
        /// <summary>Wraps a validated immutable value.</summary>
        internal DefinitionGoo(T value) { Value=value; }
        /// <inheritdoc/>
        public override bool IsValid { get { return Value!=null; } }
        /// <inheritdoc/>
        public override string TypeName { get { return "GeoHopper "+typeof(T).Name; } }
        /// <inheritdoc/>
        public override string TypeDescription { get { return "Transient GeoHopper definition"; } }
        /// <inheritdoc/>
        public override IGH_Goo Duplicate() { return new DefinitionGoo<T>(Value); }
        /// <inheritdoc/>
        public override object ScriptVariable() { return Value; }
        /// <inheritdoc/>
        public override string ToString() { return Value==null?"Missing definition":Value.ToString(); }
    }

    /// <summary>Common input decoding and validation reporting for data-only components.</summary>
    public abstract class DefinitionComponent : GH_Component
    {
        /// <summary>Creates a GeoHopper component with a documented responsibility.</summary>
        protected DefinitionComponent(string name,string nickname,string description,string category)
            :base(name,nickname,description,"GeoHopper",category) {}
        /// <inheritdoc/>
        protected override Bitmap Icon { get { return ComponentIcons.Get(GetType()); } }
        /// <summary>Extracts a typed definition from generic GH data without accepting arbitrary strings.</summary>
        internal static T Unwrap<T>(object raw) where T:class
        {
            var wrapper=raw as GH_ObjectWrapper;
            if(wrapper!=null) raw=wrapper.Value;
            var goo=raw as IGH_Goo;
            if(goo!=null) raw=goo.ScriptVariable();
            var value=raw as T;
            if(value==null) throw new ArgumentException("Connect a GeoHopper "+typeof(T).Name+" output.");
            return value;
        }
        /// <summary>Reads one typed definition from a required input.</summary>
        internal static T Read<T>(IGH_DataAccess data,int index) where T:class
        {
            object raw=null;
            if(!data.GetData(index,ref raw)) throw new ArgumentException("A required GeoHopper input is missing.");
            return Unwrap<T>(raw);
        }
        /// <summary>Reports invalid user input without producing a partially valid definition.</summary>
        protected void Report(Exception error) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error,error.Message); }
    }

    /// <summary>Shared material editor with SI values; derived components select a fixed constitutive model.</summary>
    public abstract class MaterialComponent : DefinitionComponent
    {
        /// <summary>Selects MC independently of constructor state during GH parameter registration.</summary>
        protected abstract bool IsMohrCoulomb { get; }
        /// <summary>Constructs a general material component, not a wall-specific object.</summary>
        protected MaterialComponent(string name,string nickname):base(name,nickname,
            "Defines material properties in SI units. Colour controls the Rhino region preview only.","Materials") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            bool mc=IsMohrCoulomb;
            p.AddTextParameter("ID","ID","Unique material identifier (letters, digits, underscores)",GH_ParamAccess.item,mc?"Soil":"Concrete");
            p.AddNumberParameter("E [Pa]","E","Young's modulus [Pa]",GH_ParamAccess.item,mc?20e6:30e9);
            p.AddNumberParameter("Poisson ratio","nu","Dimensionless, 0 to 0.499",GH_ParamAccess.item,mc?0.3:0.2);
            p.AddNumberParameter("Unit weight [N/m3]","gamma","Unsaturated unit weight [N/m3]",GH_ParamAccess.item,mc?18000:24000);
            if(mc)
            {
                p.AddNumberParameter("Saturated weight [N/m3]","gammaSat","Saturated unit weight [N/m3]",GH_ParamAccess.item,20000);
                p.AddNumberParameter("Cohesion [Pa]","c","Effective cohesion [Pa]",GH_ParamAccess.item,5000);
                p.AddNumberParameter("Friction [rad]","phi","Effective friction angle [rad]; pi/6 equals 30 degrees",GH_ParamAccess.item,Math.PI/6);
                p.AddNumberParameter("Dilatancy [rad]","psi","Dilatancy [rad], between zero and phi",GH_ParamAccess.item,0);
            }
            p.AddColourParameter("Colour","Colour","Rhino display colour; no mechanical effect",GH_ParamAccess.item,
                mc?Color.FromArgb(163,120,74):Color.FromArgb(155,155,160));
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Material","Material","Validated material definition",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess data)
        {
            try
            {
                string id=""; double e=0,nu=0,gu=0,gs=0,c=0,phi=0,psi=0; Color colour=Color.Gray;
                if(!data.GetData(0,ref id)||!data.GetData(1,ref e)||!data.GetData(2,ref nu)||!data.GetData(3,ref gu)) return;
                gs=gu;
                if(IsMohrCoulomb && (!data.GetData(4,ref gs)||!data.GetData(5,ref c)||!data.GetData(6,ref phi)||!data.GetData(7,ref psi))) return;
                if(!data.GetData(IsMohrCoulomb?8:4,ref colour)) return;
                data.SetData(0,new DefinitionGoo<MaterialData>(new MaterialData(id,IsMohrCoulomb,e,nu,gu,gs,c,phi,psi,colour.ToArgb())));
            } catch(Exception ex) { Report(ex); }
        }
    }

    /// <summary>Non-porous linear-elastic continuum material, suitable for the initial concrete demonstration.</summary>
    public sealed class ElasticMaterialComponent : MaterialComponent
    {
        /// <summary>Creates the linear-elastic material editor.</summary>
        public ElasticMaterialComponent():base("Material - Linear Elastic","Elastic") {}
        /// <inheritdoc/>
        protected override bool IsMohrCoulomb { get { return false; } }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed01"); } }
    }
    /// <summary>Drained Mohr-Coulomb material for geotechnical continuum regions.</summary>
    public sealed class MohrCoulombMaterialComponent : MaterialComponent
    {
        /// <summary>Creates the drained soil material editor.</summary>
        public MohrCoulombMaterialComponent():base("Material - Mohr-Coulomb","MC Soil") {}
        /// <inheritdoc/>
        protected override bool IsMohrCoulomb { get { return true; } }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed02"); } }
    }

    /// <summary>Converts a closed XY polyline into an SI region and a material-coloured viewport preview.</summary>
    public sealed class RegionComponent : DefinitionComponent
    {
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly List<DisplayMaterial> materials=new List<DisplayMaterial>();
        private readonly List<Point3d[]> outlines=new List<Point3d[]>();
        /// <summary>Creates a geometry/material component without any PLAXIS side effects.</summary>
        public RegionComponent():base("Region","Region","Closed straight-edge XY contour plus material. Coordinates use Rhino document units.","Geometry") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique region identifier",GH_ParamAccess.item,"Region");
            p.AddCurveParameter("Contour","Contour","Closed straight-edge polyline in World XY (Z=0); holes are not supported",GH_ParamAccess.item);
            p.AddGenericParameter("Material","Material","GeoHopper material",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Region","Region","Validated region in metres",GH_ParamAccess.item);
            p.AddNumberParameter("Area [m2]","Area","Cross-section area [m2]; equals volume per metre of out-of-plane length",GH_ParamAccess.item);
            p.AddColourParameter("Colour","Colour","Material display colour",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void BeforeSolveInstance()
        {
            foreach(var mesh in meshes) mesh.Dispose();
            meshes.Clear(); materials.Clear(); outlines.Clear();
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess data)
        {
            try
            {
                string id=""; Curve curve=null;
                if(!data.GetData(0,ref id)||!data.GetData(1,ref curve)) return;
                var material=Read<MaterialData>(data,2);
                var doc=RhinoDoc.ActiveDoc;
                if(doc==null || doc.ModelUnitSystem==UnitSystem.None || doc.ModelUnitSystem==UnitSystem.CustomUnits)
                    throw new ArgumentException("Set a supported Rhino document unit system before defining regions.");
                double scale=RhinoMath.UnitScale(doc.ModelUnitSystem,UnitSystem.Meters);
                Rules.Range(scale,1e-12,1e12,"Rhino-to-metre scale");
                Polyline polyline;
                if(curve==null || !curve.IsClosed || !curve.TryGetPolyline(out polyline))
                    throw new ArgumentException("Provide a closed straight-edge polyline. Curved contours must be polygonized explicitly.");
                var points=new List<XY>();
                for(int i=0;i<polyline.Count-1;i++)
                {
                    Rules.Range(polyline[i].Z*scale,-Rules.Tolerance,Rules.Tolerance,"Contour Z [m]; use World XY geometry");
                    points.Add(new XY(polyline[i].X*scale,polyline[i].Y*scale));
                }
                var region=new RegionData(id,points,material);
                var mesh=new Mesh();
                foreach(var triangle in Geometry2D.Triangles(region.Points))
                {
                    int first=mesh.Vertices.Count;
                    foreach(var point in triangle) mesh.Vertices.Add(point.X/scale,point.Y/scale,0);
                    mesh.Faces.AddFace(first,first+1,first+2);
                }
                mesh.Normals.ComputeNormals(); mesh.Compact();
                meshes.Add(mesh); materials.Add(new DisplayMaterial(Color.FromArgb(material.Argb)));
                outlines.Add(region.Points.Select(p=>new Point3d(p.X/scale,p.Y/scale,0))
                    .Concat(new[]{new Point3d(region.Points[0].X/scale,region.Points[0].Y/scale,0)}).ToArray());
                data.SetData(0,new DefinitionGoo<RegionData>(region));
                data.SetData(1,region.Area); data.SetData(2,Color.FromArgb(material.Argb));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override bool IsPreviewCapable { get { return true; } }
        /// <inheritdoc/>
        public override BoundingBox ClippingBox
        {
            get { var box=BoundingBox.Empty; foreach(var mesh in meshes) box.Union(mesh.GetBoundingBox(false)); return box; }
        }
        /// <inheritdoc/>
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if(Hidden || Locked) return;
            for(int i=0;i<meshes.Count;i++) args.Display.DrawMeshShaded(meshes[i],Attributes.Selected?args.ShadeMaterial_Selected:materials[i]);
        }
        /// <inheritdoc/>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if(Hidden || Locked) return;
            foreach(var points in outlines) args.Display.DrawPolyline(points,Attributes.Selected?Color.LimeGreen:Color.FromArgb(65,65,65),1);
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed03"); } }
    }

    /// <summary>Collects general regions and checks overlap, material identity and domain bounds.</summary>
    public sealed class ModelComponent : DefinitionComponent
    {
        /// <summary>Creates the validated model assembly component.</summary>
        public ModelComponent():base("Model","Model","Collects non-overlapping regions into a plane-strain model description.","Model") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Model identifier; target project Title must be GeoHopper:ID",GH_ParamAccess.item,"Model");
            p.AddGenericParameter("Regions","Regions","All regions as a single list (flatten this input)",GH_ParamAccess.list);
            p.AddNumberParameter("Margin [m]","Margin","Extra SoilContour bounds; does not create additional soil",GH_ParamAccess.item,0);
            p.AddGenericParameter("Interfaces","Interfaces","Optional contact definitions; flatten this input",GH_ParamAccess.list); p[3].Optional=true;
            p.AddGenericParameter("Loads","Loads","Optional line load definitions; flatten this input",GH_ParamAccess.list); p[4].Optional=true;
            p.AddGenericParameter("Phases","Phases","Full phase activation snapshots; requires Mesh Settings",GH_ParamAccess.list); p[5].Optional=true;
            p.AddGenericParameter("Mesh","Mesh","Optional Mesh Settings; requires at least two phases",GH_ParamAccess.item); p[6].Optional=true;
            p.AddGenericParameter("Plates","Plates","Optional straight structural plates; flatten this input",GH_ParamAccess.list); p[7].Optional=true;
            p.AddGenericParameter("Anchors","Anchors","Optional Node-to-Node Anchor definitions; flatten this input",GH_ParamAccess.list); p[8].Optional=true;
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Model","Model","Validated immutable model",GH_ParamAccess.item);
            p.AddTextParameter("Info","Info","Summary and required target project title",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess data)
        {
            try
            {
                string id=""; double margin=0; var raw=new List<object>();
                if(!data.GetData(0,ref id)||!data.GetDataList(1,raw)||!data.GetData(2,ref margin)) return;
                var contacts=new List<object>(); var loads=new List<object>(); var phases=new List<object>(); object mesh=null;
                data.GetDataList(3,contacts); data.GetDataList(4,loads); data.GetDataList(5,phases); data.GetData(6,ref mesh);
                var plates=new List<object>(); data.GetDataList(7,plates);
                var anchors=new List<object>(); data.GetDataList(8,anchors);
                var model=new ModelData(id,raw.Select(Unwrap<RegionData>),margin,contacts.Select(Unwrap<InterfaceData>),
                    loads.Select(Unwrap<LoadData>),phases.Select(Unwrap<PhaseData>),mesh==null?null:Unwrap<MeshData>(mesh),plates.Select(Unwrap<PlateData>),anchors.Select(Unwrap<AnchorData>));
                data.SetData(0,new DefinitionGoo<ModelData>(model));
                data.SetData(1,model+"\nTarget project Title: "+ModelBuilder.Marker(model)+"\nSI: m, N, s. "+
                    (model.Analysis==null?"Geometry/materials only.":model.Analysis.Phases.Count+" dry phases; Build prepares mesh/phases, Calculate runs analysis."));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed04"); } }
    }
}

