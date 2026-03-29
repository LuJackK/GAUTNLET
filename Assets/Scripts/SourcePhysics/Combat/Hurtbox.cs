using UnityEngine;
using Fragsurf.Movement;

namespace Fragsurf.Combat {

    public class Hurtbox : MonoBehaviour {

        [Header("Definition Source")]
        [Tooltip("If true, uses this component's Definition values. If false, allows SurfCharacter to overwrite from MovementConfig.")]
        [SerializeField] private bool useComponentDefinition = true;

        public HurtboxDefinition definition;
        
        public delegate void TakeHitAction(Hitbox hitbox);
        public event TakeHitAction OnTakeHit;

        public void TakeHit(Hitbox hitbox) {
            Debug.Log($"[Hurtbox] {gameObject.name} (ID: {definition.id}) took a hit from {hitbox.definition.id}");
            OnTakeHit?.Invoke(hitbox);
        }

        public void ConfigureFromConfig(HurtboxDefinition configDefinition) {
            if (!useComponentDefinition)
                definition = configDefinition;
        }

        private void OnDrawGizmos() {
            if (definition.id == null) return;

            Gizmos.color = Color.green;
            Vector3 center = transform.TransformPoint(definition.offset);

            Matrix4x4 oldRotation = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);

            if (definition.shape == ShapeType.Box) {
                Gizmos.DrawWireCube(Vector3.zero, definition.size);
            } else {
                Gizmos.DrawWireSphere(Vector3.zero, definition.radius);
            }

            Gizmos.matrix = oldRotation;
        }
    }

}
