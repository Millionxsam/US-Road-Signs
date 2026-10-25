using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.UI.InGame;
using Unity.Entities;
using UnityEngine.Scripting;

namespace USRoadSigns.Digital
{
    /// <summary>
    /// "Message Sign" section of the selected info panel, shown for placed digital signs. The UI module
    /// (UI/USRoadSigns.mjs) registers the matching component under this type's full name.
    /// </summary>
    public partial class DigitalSignSection : InfoSectionBase
    {
        private const string Group = Mod.ModId;

        private DigitalSignInfo m_Info;
        private DigitalSignState m_State;
        private Entity m_Entity;
        private int m_Revision;

        protected override string group => nameof(DigitalSignSection);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            AddBinding(new TriggerBinding<string>(Group, "digitalSetMessage", SetMessage));
            AddBinding(new TriggerBinding<int>(Group, "digitalSetColor", value => Modify(s => s.Color = (LedColor)Clamp(value, 0, LedColors.Count - 1))));
            AddBinding(new TriggerBinding<int>(Group, "digitalSetAlign", value => Modify(s => s.Align = (TextAlign)Clamp(value, 0, 2))));
            AddBinding(new TriggerBinding<int>(Group, "digitalSetArrow", value => Modify(s => s.Arrow = (ArrowMode)Clamp(value, 0, ArrowBoard.ModeCount - 1))));
            AddBinding(new TriggerBinding<int>(Group, "digitalSetPageMs", value => Modify(s => s.PageMs = Clamp(value, DigitalSignState.MinPageMs, DigitalSignState.MaxPageMs))));
            AddBinding(new TriggerBinding<bool>(Group, "digitalSetFlash", value => Modify(s => s.Flash = value)));
            AddBinding(new TriggerBinding<bool>(Group, "digitalSetBeacons", value => Modify(s => s.Beacons = value)));
            AddBinding(new TriggerBinding(Group, "digitalReset", ResetToDefaults));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            visible = false;
            m_Info = null;
            if (selectedEntity == Entity.Null || !EntityManager.HasComponent<DigitalSign>(selectedEntity))
                return;
            if (!EntityManager.TryGetComponent(selectedPrefab, out DigitalSignData data))
                return;
            m_Info = DigitalSignLibrary.Get(data.m_TypeIndex);
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
            m_State = DigitalSignState.Resolve(m_Info.Type, EntityManager.GetComponentData<DigitalSign>(selectedEntity));
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            DigitalSignType type = m_Info.Type;
            bool text = type.Display == DisplayKind.Text;

            writer.PropertyName("signKey");
            writer.Write(m_Entity.Index + ":" + m_Entity.Version + ":" + m_Revision);
            writer.PropertyName("kind");
            writer.Write(text ? "text" : "arrow");
            writer.PropertyName("lines");
            writer.Write(type.Lines);
            writer.PropertyName("matrix");
            writer.Write(text ? type.Columns + " x " + type.Rows : ArrowBoard.Columns + " x " + ArrowBoard.Rows);
            writer.PropertyName("maxPages");
            writer.Write(SignMessage.MaxPages);
            writer.PropertyName("maxLineLength");
            writer.Write(SignMessage.MaxLineLength);

            // Pages, with a flag per line that did not fit the display width.
            List<string[]> pages = text ? SignMessage.Parse(m_State.Message, type.Lines) : new List<string[]>();
            writer.PropertyName("pages");
            writer.ArrayBegin(pages.Count);
            foreach (string[] page in pages)
            {
                writer.ArrayBegin(page.Length);
                foreach (string line in page)
                    writer.Write(line);
                writer.ArrayEnd();
            }
            writer.ArrayEnd();
            writer.PropertyName("overflow");
            writer.ArrayBegin(pages.Count);
            foreach (string[] page in pages)
            {
                MessageLayout.RenderPage(page, type.Columns, type.Rows, m_State.Align, out bool[] overflow);
                writer.ArrayBegin(overflow.Length);
                foreach (bool o in overflow)
                    writer.Write(o);
                writer.ArrayEnd();
            }
            writer.ArrayEnd();

            writer.PropertyName("color");
            writer.Write((int)m_State.Color);
            writer.PropertyName("colors");
            writer.ArrayBegin(LedColors.Count);
            for (int i = 0; i < LedColors.Count; i++)
                writer.Write(LedColors.Name((LedColor)i));
            writer.ArrayEnd();
            writer.PropertyName("colorSwatches");
            writer.ArrayBegin(LedColors.Count);
            for (int i = 0; i < LedColors.Count; i++)
            {
                LedColors.Get((LedColor)i, out byte r, out byte g, out byte b);
                writer.Write("rgb(" + r + "," + g + "," + b + ")");
            }
            writer.ArrayEnd();

            writer.PropertyName("align");
            writer.Write((int)m_State.Align);
            writer.PropertyName("flash");
            writer.Write(m_State.Flash);
            writer.PropertyName("pageMs");
            writer.Write(m_State.PageMs);
            writer.PropertyName("hasBeacons");
            writer.Write(type.HasBeacons);
            writer.PropertyName("beacons");
            writer.Write(m_State.Beacons);

            writer.PropertyName("arrow");
            writer.Write((int)m_State.Arrow);
            writer.PropertyName("arrowModes");
            writer.ArrayBegin(text ? 0 : ArrowBoard.ModeCount);
            if (!text)
            {
                for (int i = 0; i < ArrowBoard.ModeCount; i++)
                    writer.Write(ArrowBoard.ModeName((ArrowMode)i));
            }
            writer.ArrayEnd();
        }

        // ------------------------------------------------------------------------------------------
        // Triggers from the UI
        // ------------------------------------------------------------------------------------------

        private void SetMessage(string message)
        {
            if (m_Info == null || m_Info.Type.Display != DisplayKind.Text)
                return;
            // Normalize through the parser: line count, upper case, supported characters, page limit.
            string normalized = SignMessage.Format(SignMessage.Parse(message, m_Info.Type.Lines));
            Modify(s => s.Message = normalized, resync: false);
        }

        private void ResetToDefaults()
        {
            if (!CanEdit())
                return;
            EntityManager.SetComponentData(selectedEntity, default(DigitalSign));
            Changed(resync: true);
        }

        private void Modify(Action<Box> edit, bool resync = false)
        {
            if (!CanEdit())
                return;
            var box = new Box { State = DigitalSignState.Resolve(m_Info.Type, EntityManager.GetComponentData<DigitalSign>(selectedEntity)) };
            edit(box);
            EntityManager.SetComponentData(selectedEntity, box.State.ToComponent());
            Changed(resync);
        }

        private bool CanEdit()
        {
            return m_Info != null && selectedEntity != Entity.Null && EntityManager.HasComponent<DigitalSign>(selectedEntity);
        }

        private void Changed(bool resync)
        {
            // A resync makes the panel reload its text fields (reset); typing does not, so the caret stays put.
            if (resync)
                m_Revision++;
            RequestUpdate();
            m_InfoUISystem.RequestUpdate();
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        /// <summary>Mutable holder so lambdas can edit the state struct.</summary>
        private sealed class Box
        {
            public DigitalSignState State;

            public LedColor Color { set => State.Color = value; }
            public TextAlign Align { set => State.Align = value; }
            public ArrowMode Arrow { set => State.Arrow = value; }
            public int PageMs { set => State.PageMs = value; }
            public bool Flash { set => State.Flash = value; }
            public bool Beacons { set => State.Beacons = value; }
            public string Message { set => State.Message = value; }
        }
    }
}
