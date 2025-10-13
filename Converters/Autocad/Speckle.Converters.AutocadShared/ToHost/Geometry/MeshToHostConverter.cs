using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Models;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Speckle.Converters.Autocad.Geometry;


[NameAndRankValue(typeof(SOG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Mesh, ADB.Entity>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public MeshToHostConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Mesh)target);

  public ADB.Entity Convert(SOG.Mesh target)
  {

    // Decide: "solid" -> Solid3d ; else -> PolyFaceMesh
    string? c3dType = TryGetC3DObjectType(target);


    if (string.Equals(c3dType, "solid", StringComparison.OrdinalIgnoreCase))
    {
      return CreateSolidFromMesh(target);     // -> Solid3d
    }
    else
    {
      return CreatePolyFaceMeshFromMesh(target); // -> PolyFaceMesh
    }
      
  }

  /// <summary>Creates a Solid3d by sculpting from planar Surfaces created from 3D Faces.</summary>
  private ADB.Solid3d CreateSolidFromMesh(SOG.Mesh target)
  { 

    target.TriangulateMesh(true);

    // get vertex points
    using AG.Point3dCollection vertices = new();
    List<AG.Point3d> points = target.GetPoints().Select(o => _pointConverter.Convert(o)).ToList();
    foreach (var point in points)
    {
      vertices.Add(point);
    }

    ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.TopTransaction;

    // append mesh to blocktable record - necessary before adding vertices and faces
    var btr = (ADB.BlockTableRecord)
    tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);


    // Create surfaces from mesh faces
    DBObjectCollection surfaces = new DBObjectCollection();


    int j = 0;
    while (j < target.faces.Count)
    {
      AG.Point3d p1, p2, p3, p4;
      if (target.faces[j] == 3) // triangle
      {
        p1 = points[target.faces[j + 1]];
        p2 = points[target.faces[j + 2]];
        p3 = points[target.faces[j + 3]];

        using (var f = new Autodesk.AutoCAD.DatabaseServices.Face())
        {
          // A 3DFace always has 4 vertices; for a triangle, repeat the 3rd point
          f.SetVertexAt(0, p1);
          f.SetVertexAt(1, p2);
          f.SetVertexAt(2, p3);
          f.SetVertexAt(3, p3); // duplicate for triangular face
                                // Alternatively:
                                // using var f = new Face(p1, p2, p3, p3, true, true, true, true);

          var s = Autodesk.AutoCAD.DatabaseServices.Surface.CreateFrom(f);
          surfaces.Add(s);


        }

        j += 4;
      }
      else // quad
      {
        p1 = points[target.faces[j + 1]];
        p2 = points[target.faces[j + 2]];
        p3 = points[target.faces[j + 3]];
        p4 = points[target.faces[j + 4]];


        using (var f = new Autodesk.AutoCAD.DatabaseServices.Face(p1, p2, p3, p4, true, true, true, true))
        {
          var s = Autodesk.AutoCAD.DatabaseServices.Surface.CreateFrom(f);
          surfaces.Add(s);


        }


        j += 5;
      }
    }

    // Create solid from surfaces
    ADB.Solid3d solid = new ADB.Solid3d();
    solid.SetDatabaseDefaults();

    var flags = new IntegerCollection(); // can be empty
    solid.CreateSculptedSolid(surfaces.Cast<Autodesk.AutoCAD.DatabaseServices.Entity>().ToArray(), flags);

    btr.AppendEntity(solid);
    tr.AddNewlyCreatedDBObject(solid, true);


    return solid;

  }


  /// <summary>Creates a PolyFaceMesh (classic "poly-mesh") from Speckle Mesh.</summary>
  private ADB.PolyFaceMesh CreatePolyFaceMeshFromMesh(SOG.Mesh target)
  {
    target.TriangulateMesh(true);

    // get vertex points
    using AG.Point3dCollection vertices = new();
    List<AG.Point3d> points = target.GetPoints().Select(o => _pointConverter.Convert(o)).ToList();
    foreach (var point in points)
    {
      vertices.Add(point);
    }

    ADB.PolyFaceMesh mesh = new();

    //TODO using?
    ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.TopTransaction;

    mesh.SetDatabaseDefaults();

    // append mesh to blocktable record - necessary before adding vertices and faces
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);
    btr.AppendEntity(mesh);
    tr.AddNewlyCreatedDBObject(mesh, true);

    // add polyfacemesh vertices
    for (int i = 0; i < vertices.Count; i++)
    {
      var vertex = new ADB.PolyFaceMeshVertex(points[i]);
      if (i < target.colors.Count)
      {
        try
        {
          if (System.Drawing.Color.FromArgb(target.colors[i]) is System.Drawing.Color color)
          {
            vertex.Color = Autodesk.AutoCAD.Colors.Color.FromRgb(color.R, color.G, color.B);
          }
        }
        catch (System.Exception e) when (!e.IsFatal())
        {
          // POC: should we warn user?
          // Couldn't set vertex color, but this should not prevent conversion.
        }
      }

      if (vertex.IsNewObject)
      {
        mesh.AppendVertex(vertex);
        tr.AddNewlyCreatedDBObject(vertex, true);
      }
    }

    // add polyfacemesh faces. vertex index starts at 1 sigh
    int j = 0;
    while (j < target.faces.Count)
    {
      ADB.FaceRecord face;
      if (target.faces[j] == 3) // triangle
      {
        face = new ADB.FaceRecord(
          (short)(target.faces[j + 1] + 1),
          (short)(target.faces[j + 2] + 1),
          (short)(target.faces[j + 3] + 1),
          0
        );
        j += 4;
      }
      else // quad
      {
        face = new ADB.FaceRecord(
          (short)(target.faces[j + 1] + 1),
          (short)(target.faces[j + 2] + 1),
          (short)(target.faces[j + 3] + 1),
          (short)(target.faces[j + 4] + 1)
        );
        j += 5;
      }

      if (face.IsNewObject)
      {
        mesh.AppendFaceRecord(face);
        tr.AddNewlyCreatedDBObject(face, true);
      }
    }

    return mesh;
  }


  // add inside MeshToHostConverter class
  private static string? TryGetC3DObjectType(Base target)
  {

    if (target is null)
    {
      return null;
    }

    if (target["properties"] is Dictionary<string, object?> properties)
    {
      if (properties.TryGetValue("C3D Object Type", out var obj_type) && obj_type is Dictionary<string, object> type)
      {
        if (type.TryGetValue("Type", out var c3dtype) && c3dtype is String string_type)
        {
          return string_type;
        }
      }
    }
    return null;
  }


}
