using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace USRoadSigns.Route
{
    [Flags]
    public enum RouteSignFlags : byte
    {
        None = 0,
        /// <summary>The player changed the sign; otherwise the type's default is shown.</summary>
        Customized = 1,
    }

    /// <summary>
    /// Per-sign settings of a route marker assembly or destination sign, saved with the city.
    /// <see cref="m_State"/> holds <see cref="RouteSignState.Encode"/> output.
    /// </summary>
    public struct RouteSign : IComponentData, IQueryTypeParameter, ISerializable
    {
        private const int kVersion = 1;

        public FixedString512Bytes m_State;
        public RouteSignFlags m_Flags;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write((int)m_Flags);
            writer.Write(m_State.ToString());
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out int flags);
            reader.Read(out string state);
            m_Flags = (RouteSignFlags)flags;
            SetState(state);
        }

        /// <summary>Largest state stored (FixedString512Bytes holds 509 UTF-8 bytes; states are ASCII).</summary>
        public const int MaxStateBytes = 500;

        public void SetState(string state)
        {
            state = state ?? "";
            m_State = state.Length > MaxStateBytes ? state.Substring(0, MaxStateBytes) : state;
        }
    }

    /// <summary>On route sign prefabs: which <see cref="RouteSignTypes.All"/> entry, and whether it is the flashing variant.</summary>
    public struct RouteSignData : IComponentData, IQueryTypeParameter
    {
        public int m_TypeIndex;
        public bool m_Flashing;
    }

    /// <summary>Prefab component of route signs: adds <see cref="RouteSignData"/> to the prefab and <see cref="RouteSign"/> to every placed sign.</summary>
    public class RouteSignPrefab : ComponentBase
    {
        public int m_TypeIndex;
        public bool m_Flashing;

        public override void GetPrefabComponents(HashSet<ComponentType> components)
        {
            components.Add(ComponentType.ReadWrite<RouteSignData>());
        }

        public override void GetArchetypeComponents(HashSet<ComponentType> components)
        {
            components.Add(ComponentType.ReadWrite<RouteSign>());
        }

        public override void Initialize(EntityManager entityManager, Entity entity)
        {
            base.Initialize(entityManager, entity);
            entityManager.SetComponentData(entity, new RouteSignData { m_TypeIndex = m_TypeIndex, m_Flashing = m_Flashing });
        }
    }
}
