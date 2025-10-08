using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;
//using Speckle.Sdk;
using Speckle.Sdk.Models;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Speckle.Converters.Autocad.Geometry;


[NameAndRankValue(typeof(SOG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Mesh, ADB.Solid3d>
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




  /// <remarks>
  /// Mesh conversion requires transaction since it's vertices needed to be added into database in advance..
  /// </remarks>
  public ADB.Solid3d Convert(SOG.Mesh target)
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
}
