//using Autodesk.AutoCAD.Runtime;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;


/// <summary>
/// Extracts object type from a dbobject. Expects to be scoped per operation.
/// </summary>
public class ObjectTypeExtractor
{
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)

  public ObjectTypeExtractor(
  )
  {
  }

  /// <summary>
  /// Extracts object typet from a dbObject. Expects to be scoped per operation.
  /// </summary>
  /// <param name="dbObject"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetObjectType(ADB.DBObject dbObject)
  {

    var result = new Dictionary<string, object?>();

    var c3dType = GetCivil3dObjectType(dbObject);
    result.Add("Type", c3dType);

    return result;

  }

  private static string GetCivil3dObjectType(ADB.DBObject dbo)
  { 

    if (dbo is null)
    {
      return "unknown";
    }
        
    // Preferred: DXF name from the runtime class (covers both AECC_* and ACAD types)
    var dxf = dbo.GetRXClass()?.DxfName ?? string.Empty;
    if (dxf.Length > 0)
    {
      //// Normalize Civil 3D names: AECC_TIN_SURFACE -> "Tin Surface", AECC_ALIGNMENT -> "Alignment"
      //if (dxf.StartsWith("AECC_", StringComparison.OrdinalIgnoreCase))
      //  return ToTitleCase(dxf.AsSpan(5).ToString().Replace('_', ' '));

      // Normalize a few common AutoCAD entity names to friendlier words
      switch (dxf.ToUpperInvariant())
      {
        case "3DSOLID": return "solid";
        case "LWPOLYLINE": return "polyline";
        case "POLYLINE": return "polyline";
        case "SPLINE": return "spline";
        case "LINE": return "line";
        default: return dxf.ToLowerInvariant();
      }
    }

    // Fallback if DXF is unavailable (rare): use .NET type name
    return dbo.GetType().Name;
      
  }

}
