using UnityEngine;

namespace Game.View
{
    // A cylinder's horizontal footprint feeds the same LayoutMap as movement and attacks.
    // Move/scale it before Play; edits during Play are applied by Reset fight.
    [DisallowMultipleComponent]
    public sealed class EnemyTestObstacle : MonoBehaviour
    {
        public float WorldRadius => Mathf.Max(.1f,
            Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * .5f);
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, 0, transform.position.z), WorldRadius);
        }
    }
}
