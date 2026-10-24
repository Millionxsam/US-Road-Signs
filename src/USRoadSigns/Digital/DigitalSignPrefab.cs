using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;

namespace USRoadSigns.Digital
{
    /// <summary>
    /// Prefab component of digital message signs. Marks the prefab with <see cref="DigitalSignData"/> and gives every
    /// placed instance a <see cref="DigitalSign"/> (ObjectPrefab builds the instance archetype from all components).
    /// </summary>
    public class DigitalSignPrefab : ComponentBase
    {
        public int m_TypeIndex;

        public override void GetPrefabComponents(HashSet<ComponentType> components)
        {
            components.Add(ComponentType.ReadWrite<DigitalSignData>());
        }

        public override void GetArchetypeComponents(HashSet<ComponentType> components)
        {
            components.Add(ComponentType.ReadWrite<DigitalSign>());
        }

        public override void Initialize(EntityManager entityManager, Entity entity)
        {
            base.Initialize(entityManager, entity);
            entityManager.SetComponentData(entity, new DigitalSignData { m_TypeIndex = m_TypeIndex });
        }
    }
}
