using UnityEngine;
using Fragsurf.Movement;

namespace Fragsurf.Combat {

    public class Hurtbox : MonoBehaviour {

        public HurtboxDefinition definition;
        
        public delegate void TakeHitAction(Hitbox hitbox);
        public event TakeHitAction OnTakeHit;

        public void TakeHit(Hitbox hitbox) {
            Debug.Log($"[Hurtbox] {gameObject.name} (ID: {definition.id}) took a hit from {hitbox.definition.id}");
            OnTakeHit?.Invoke(hitbox);
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
