// Shared by the CS2 mod (USRoadSigns) and the offline build tool (SignForge).
// Pure C# only: no UnityEngine / game references allowed in this folder.
//
// All physical dimensions in the catalog are in INCHES (the native unit of the MUTCD and the
// FHWA Standard Highway Signs book). Geometry code converts to meters with Units.InchesToMeters.

using System;
using System.Collections.Generic;

namespace USRoadSigns.Catalog
{
    /// <summary>Root of data/catalog.json: global configuration shared by every sign.</summary>
    public sealed class CatalogRoot
    {
        public int FormatVersion = 1;

        /// <summary>Prefix for every generated prefab name. Prefab names must never change once released,
        /// because saved cities reference placed objects by prefab name.</summary>
        public string PrefabPrefix = "USRS_";

        public MenuDef Menu = new MenuDef();
        public List<CategoryDef> Categories = new List<CategoryDef>();
        public Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public MountingDefaults Mounting = new MountingDefaults();
    }

    /// <summary>The top-level toolbar menu ("US Road Signs").</summary>
    public sealed class MenuDef
    {
        public string Id = "USRoadSigns";
        public string Name = "US Road Signs";
        public string Description = "";
        /// <summary>Icon path relative to the mod folder.</summary>
        public string Icon = "";
        /// <summary>Face rendered by the build tool to create the icon.</summary>
        public string IconFace;
        public int Priority = 1010;
    }

    /// <summary>A tab inside the menu (Regulatory, Warning, ...).</summary>
    public sealed class CategoryDef
    {
        public string Id;
        public string Name;
        public string Description = "";
        /// <summary>Icon path relative to the mod folder.</summary>
        public string Icon = "";
        /// <summary>Face rendered by the build tool to create the icon.</summary>
        public string IconFace;
        public int Priority;
    }

    /// <summary>Default mounting rules (MUTCD section 2A.18 urban mounting unless overridden).</summary>
    public sealed class MountingDefaults
    {
        /// <summary>Height of the bottom of the lowest panel above the ground (84 in = 7 ft, urban).</summary>
        public float MountHeight = 84f;

        /// <summary>Vertical gap between stacked panels (main sign and supplemental plaques).</summary>
        public float PanelGap = 1f;

        /// <summary>How far the post extends above the top panel.</summary>
        public float PostTopClearance = 1.5f;

        /// <summary>How far posts extend below ground level so they look embedded on sloped terrain.</summary>
        public float PostBurial = 6f;

        /// <summary>Aluminum sign blank thickness (0.080 in - 0.125 in in practice).</summary>
        public float PlateThickness = 0.125f;
    }

    // ------------------------------------------------------------------------------------------
    // Faces: the printed artwork of one sign blank (one texture per face).
    // ------------------------------------------------------------------------------------------

    public sealed class FaceFile
    {
        public List<FaceDef> Faces = new List<FaceDef>();
    }

    public sealed class FaceDef
    {
        /// <summary>Unique face id, usually the MUTCD code (e.g. "W11-2"). Also the texture file name.</summary>
        public string Id;

        /// <summary>Plate outline: rect, diamond, octagon, triangle, pentagon, circle, crossbuck, pennant, shield-interstate.</summary>
        public string Shape = "rect";

        /// <summary>Nominal size in inches as listed by the MUTCD. For "diamond" and "triangle" these are side lengths.</summary>
        public float Width;
        public float Height;

        /// <summary>Corner radius of the aluminum blank (inches).</summary>
        public float CornerRadius;

        /// <summary>Background color name (see catalog colors) or #RRGGBB.</summary>
        public string Background = "white";

        public BorderDef Border;

        /// <summary>Artwork elements, drawn in order. Coordinates are inches from the top-left corner of the
        /// plate's bounding box (y grows downward, like SVG).</summary>
        public List<ElementDef> Elements = new List<ElementDef>();

        /// <summary>Optional texture size overrides (power of two). 0 = automatic.</summary>
        public int TextureWidth;
        public int TextureHeight;

        /// <summary>Optional free-form note for maintainers.</summary>
        public string Note;
    }

    public sealed class BorderDef
    {
        public string Color = "black";
        /// <summary>Border stroke width (inches).</summary>
        public float Width;
        /// <summary>Distance from the plate edge to the outer edge of the border (inches).</summary>
        public float Inset;
        /// <summary>Corner radius of the outer edge of the border (inches).</summary>
        public float Radius;
    }

    public sealed class ElementDef
    {
        /// <summary>text | symbol | arrow | rect | ellipse | polygon | path | shield</summary>
        public string Type;

        // Common placement: center point and box size in inches.
        public float X;
        public float Y;
        public float Width;
        public float Height;
        /// <summary>Clockwise rotation in degrees around (X, Y).</summary>
        public float Rotation;
        public bool MirrorX;
        public string Color = "black";
        public string StrokeColor;
        public float StrokeWidth;
        public float CornerRadius;

        // text
        public string Text;
        public List<string> Lines;
        /// <summary>FHWA series approximation: B, C, D, E, EM, F.</summary>
        public string Font = "D";
        /// <summary>Upper-case letter height in inches (MUTCD "letter height").</summary>
        public float Size;
        /// <summary>Distance between baselines for multi-line text (inches). 0 = 1.55 x Size.</summary>
        public float LineSpacing;
        /// <summary>center | left | right</summary>
        public string Align = "center";
        /// <summary>If &gt; 0, text wider than this is horizontally compressed to fit (inches).</summary>
        public float MaxWidth;
        /// <summary>Extra letter spacing as a fraction of letter height.</summary>
        public float Tracking;

        // symbol
        public string Symbol;

        // arrow (straight arrow from tail X1,Y1 to tip X2,Y2)
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;
        public float ShaftWidth;
        public float HeadWidth;
        public float HeadLength;

        // polygon (flat list x0,y0,x1,y1,...) and path (SVG/WPF path markup in face inches)
        public List<float> Points;
        public string Data;

        // shield
        /// <summary>interstate | us | state</summary>
        public string Kind;
    }

    // ------------------------------------------------------------------------------------------
    // Signs: placeable assemblies (panels + support). One game prefab per sign.
    // ------------------------------------------------------------------------------------------

    public sealed class SignFile
    {
        public List<SignDef> Signs = new List<SignDef>();
    }

    public sealed class SignDef
    {
        /// <summary>Stable unique id. The prefab name is CatalogRoot.PrefabPrefix + Id: never rename after release.</summary>
        public string Id;

        /// <summary>Display name shown in the game UI.</summary>
        public string Name;

        /// <summary>Category id (see catalog.json).</summary>
        public string Category;

        /// <summary>MUTCD designation(s) for the description, e.g. "W11-2 + W16-7P".</summary>
        public string Code;

        public string Description;

        /// <summary>Sort order inside the category. 0 = order of appearance in the file.</summary>
        public int Priority;

        /// <summary>Panels from top to bottom.</summary>
        public List<PanelDef> Panels = new List<PanelDef>();

        public SupportDef Support = new SupportDef();

        /// <summary>In-game construction cost. -1 = automatic from sign area.</summary>
        public int ConstructionCost = -1;
    }

    public sealed class PanelDef
    {
        /// <summary>Face id.</summary>
        public string Face;
        /// <summary>Horizontal offset of the panel center from the assembly center (inches, + = viewer's right).</summary>
        public float OffsetX;
        /// <summary>Gap above this panel (inches). -1 = catalog default.</summary>
        public float GapAbove = -1f;
    }

    public sealed class SupportDef
    {
        /// <summary>
        /// post            single post, style chosen by the player in the mod settings
        /// square-tube     2 in perforated square steel tube
        /// u-channel       2 lb/ft galvanized U-channel
        /// round-pipe      2-3/8 in galvanized pipe with clamp brackets
        /// twin-square     two 2-1/2 in square tube posts (wide signs)
        /// twin-ibeam      two W6x9 steel posts with Z-bar stiffeners (large guide signs)
        /// portable-stand  temporary spring sign stand
        /// barricade       Type III barricade
        /// flex-post       in-street flexible delineator post
        /// </summary>
        public string Type = "post";

        /// <summary>Height of the bottom of the lowest panel above ground (inches). -1 = default.</summary>
        public float MountHeight = -1f;

        /// <summary>Distance between post centers for twin supports (inches). -1 = automatic.</summary>
        public float Spacing = -1f;

        /// <summary>Post extension above the top panel (inches). -1 = default.</summary>
        public float TopClearance = -1f;
    }

    // ------------------------------------------------------------------------------------------
    // Symbols: vector pictograms used by faces (only needed by the build tool).
    // ------------------------------------------------------------------------------------------

    public sealed class SymbolFile
    {
        public List<SymbolDef> Symbols = new List<SymbolDef>();
    }

    public sealed class SymbolDef
    {
        public string Id;
        /// <summary>minX, minY, width, height of the design space.</summary>
        public float[] ViewBox;
        public List<SymbolPart> Parts = new List<SymbolPart>();
    }

    public sealed class SymbolPart
    {
        /// <summary>SVG-style path data (M, L, H, V, C, Q, A, Z).</summary>
        public string Path;
        /// <summary>Fill color: "fg" (element color), "bg" (face background), a catalog color name, or "none".</summary>
        public string Fill = "fg";
        /// <summary>Stroke color (same rules as Fill). Null or "none" = no stroke.</summary>
        public string Stroke;
        public float StrokeWidth;
        /// <summary>round | flat | square</summary>
        public string LineCap = "round";
        /// <summary>round | miter | bevel</summary>
        public string LineJoin = "round";
    }
}
