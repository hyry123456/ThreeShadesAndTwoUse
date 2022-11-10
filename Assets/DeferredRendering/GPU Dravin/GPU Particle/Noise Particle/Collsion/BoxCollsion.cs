using UnityEngine;


namespace DefferedRender
{
    public class BoxCollsion : IGetCollsion
    {
        public Vector3 cubeOffset = Vector3.one;

        public override CollsionStruct GetCollsionStruct()
        {
            CollsionStruct collsion = new CollsionStruct();
            collsion.mode = 0;
            collsion.center = transform.position;
            collsion.offset = cubeOffset;
            return collsion;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, cubeOffset * 2);
        }
#endif
    }
}