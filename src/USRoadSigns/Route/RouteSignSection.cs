using System;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine.Scripting;

namespace USRoadSigns.Route
{
    /// <summary>
    /// "Route sign" section of the selected info panel, shown for placed route marker assemblies and destination
    /// signs. The UI module (UI/USRoadSigns.mjs) registers the matching component under this type's full name.
    /// </summary>
    public partial class RouteSignSection : InfoSectionBase
    {
        private const string Group = Mod.ModId;
        private const char FieldSeparator = '\n';

        private RouteSignInfo m_Info;
        private RouteSignState m_State;
        private Entity m_Entity;
        private int m_Revision;

        protected override string group => nameof(RouteSignSection);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            // "column|line \n index \n field \n value": one trigger for every field keeps the UI module simple.
            AddBinding(new TriggerBinding<string>(Group, "routeSet", Set));
            AddBinding(new TriggerBinding(Group, "routeReset", ResetToDefaults));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            visible = false;
            m_Info = null;
            if (selectedEntity == Entity.Null || !EntityManager.HasComponent<RouteSign>(selectedEntity))
                return;
            if (!EntityManager.TryGetComponent(selectedPrefab, out RouteSignData data))
                return;
            m_Info = RouteSignLibrary.Get(data.m_TypeIndex, data.m_Flashing);
            visible = m_Info != null;
        }

        protected override void Reset()
        {
        }

        [Preserve]
        protected override void OnProcess()
        {
            if (m_Entity != selectedEntity)
            {
                m_Entity = selectedEntity;
                m_Revision++;
            }
            m_State = Current();
        }

        private RouteSignState Current()
        {
            RouteSign sign = EntityManager.GetComponentData<RouteSign>(selectedEntity);
            string encoded = (sign.m_Flags & RouteSignFlags.Customized) != 0 ? sign.m_State.ToString() : m_Info.DefaultState;
            return RouteSignState.Decode(m_Info.Type, encoded);
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            RouteSignType type = m_Info.Type;
            bool marker = type.Kind == RouteSignKind.Marker;

            writer.PropertyName("signKey");
            writer.Write(m_Entity.Index + ":" + m_Entity.Version + ":" + m_Revision);
            writer.PropertyName("kind");
            writer.Write(marker ? "marker" : "destination");
            writer.PropertyName("hasAux");
            writer.Write(type.HasAux);
            writer.PropertyName("hasArrow");
            writer.Write(type.HasArrow);
            writer.PropertyName("maxNumber");
            writer.Write(RouteSignState.MaxNumberLength);
            writer.PropertyName("maxName");
            writer.Write(RouteSignState.MaxNameLength);
            writer.PropertyName("maxText");
            writer.Write(RouteSignState.MaxTextLength);
            writer.PropertyName("maxDistance");
            writer.Write(RouteSignState.MaxDistanceLength);

            writer.PropertyName("columns");
            writer.ArrayBegin(m_State.Columns.Length);
            foreach (RouteColumn c in m_State.Columns)
            {
                writer.TypeBegin("USRoadSigns.RouteColumn");
                writer.PropertyName("aux");
                writer.Write((int)c.Aux);
                writer.PropertyName("cardinal");
                writer.Write((int)c.Cardinal);
                writer.PropertyName("shield");
                writer.Write((int)c.Shield);
                writer.PropertyName("number");
                writer.Write(c.Number);
                writer.PropertyName("name");
                writer.Write(c.Name);
                writer.PropertyName("arrow");
                writer.Write((int)c.Arrow);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.PropertyName("plates");
            writer.Write((int)m_State.Plates);

            writer.PropertyName("lines");
            writer.ArrayBegin(m_State.Lines.Length);
            foreach (DestinationLine l in m_State.Lines)
            {
                writer.TypeBegin("USRoadSigns.DestinationLine");
                writer.PropertyName("text");
                writer.Write(l.Text);
                writer.PropertyName("arrow");
                writer.Write((int)l.Arrow);
                writer.PropertyName("distance");
                writer.Write(l.Distance);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.PropertyName("color");
            writer.Write((int)m_State.Color);
            writer.PropertyName("dividers");
            writer.Write(m_State.Dividers);

            // Option lists: [value, label] pairs flattened, so the UI shows them in a sensible order.
            WriteOptions(writer, "shieldOptions", Enum.GetValues(typeof(ShieldKind)), v => RouteSignState.Name((ShieldKind)v));
            WriteOptions(writer, "cardinalOptions", Enum.GetValues(typeof(CardinalKind)), v => v == 0 ? "None" : RouteSignState.Legend((CardinalKind)v));
            WriteOptions(writer, "auxOptions", Enum.GetValues(typeof(AuxKind)), v => v == 0 ? "None" : RouteSignState.Legend((AuxKind)v));
            WriteOptions(writer, "arrowOptions", Enum.GetValues(typeof(ArrowKind)), v => RouteSignState.Name((ArrowKind)v));
            WriteOptions(writer, "destinationArrowOptions", RouteSignState.DestinationArrows, v => RouteSignState.Name((ArrowKind)v));
            WriteOptions(writer, "colorOptions", Enum.GetValues(typeof(GuideColor)), v => RouteSignState.Name((GuideColor)v));
        }

        private static void WriteOptions(IJsonWriter writer, string name, Array values, Func<int, string> label)
        {
            writer.PropertyName(name);
            writer.ArrayBegin(values.Length * 2);
            foreach (object value in values)
            {
                int v = Convert.ToInt32(value);
                writer.Write(v.ToString());
                writer.Write(label(v));
            }
            writer.ArrayEnd();
        }

        // ------------------------------------------------------------------------------------------
        // Triggers from the UI
        // ------------------------------------------------------------------------------------------

        private void Set(string message)
        {
            if (!CanEdit() || string.IsNullOrEmpty(message))
                return;
            string[] f = message.Split(FieldSeparator);
            if (f.Length < 4)
                return;
            string target = f[0], field = f[2], value = f[3];
            int.TryParse(f[1], out int index);
            int.TryParse(value, out int number);

            RouteSignState state = Current();
            bool resync = false;
            if (target == "column" && index >= 0 && index < state.Columns.Length)
            {
                RouteColumn c = state.Columns[index];
                switch (field)
                {
                    case "aux": c.Aux = (AuxKind)number; break;
                    case "cardinal": c.Cardinal = (CardinalKind)number; break;
                    case "shield": c.Shield = (ShieldKind)number; break;
                    case "arrow": c.Arrow = (ArrowKind)number; break;
                    case "number": c.Number = value; break;
                    case "name": c.Name = value; break;
                    default: return;
                }
            }
            else if (target == "line" && index >= 0 && index < state.Lines.Length)
            {
                DestinationLine l = state.Lines[index];
                switch (field)
                {
                    case "text": l.Text = value; break;
                    case "distance": l.Distance = value; break;
                    case "arrow": l.Arrow = (ArrowKind)number; break;
                    default: return;
                }
            }
            else if (target == "sign")
            {
                switch (field)
                {
                    case "plates": state.Plates = (PlateStyle)number; break;
                    case "color": state.Color = (GuideColor)number; break;
                    case "dividers": state.Dividers = number != 0; break;
                    default: return;
                }
                resync = true;
            }
            else
            {
                return;
            }

            state.Normalize(m_Info.Type);
            var sign = new RouteSign { m_Flags = RouteSignFlags.Customized };
            sign.SetState(state.Encode(m_Info.Type));
            EntityManager.SetComponentData(selectedEntity, sign);
            Changed(resync);
        }

        private void ResetToDefaults()
        {
            if (!CanEdit())
                return;
            EntityManager.SetComponentData(selectedEntity, default(RouteSign));
            Changed(resync: true);
        }

        private bool CanEdit()
        {
            return m_Info != null && selectedEntity != Entity.Null && EntityManager.HasComponent<RouteSign>(selectedEntity);
        }

        private void Changed(bool resync)
        {
            // A resync makes the panel reload its text fields (reset); typing does not, so the caret stays put.
            if (resync)
                m_Revision++;
            RequestUpdate();
            m_InfoUISystem.RequestUpdate();
        }
    }
}
