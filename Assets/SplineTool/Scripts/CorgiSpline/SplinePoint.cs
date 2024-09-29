using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorgiSpline
{

    [System.Serializable]
    public struct SplinePoint
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Color color;

        public SplinePoint(Vector3 position, Quaternion rotation, Vector3 scale, Color color)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
            this.color = color;
        }

        public override bool Equals(object obj)
        {
            var otherPoint = (SplinePoint)obj;
            var matchPosition = (position - otherPoint.position).sqrMagnitude < 0.001f;
            var matchColor = ((Vector4)color - (Vector4)otherPoint.color).sqrMagnitude < 0.001f;
            var matchUp = (rotation.eulerAngles - otherPoint.rotation.eulerAngles).sqrMagnitude < 0.001f;
            var matchScale = (scale - otherPoint.scale).sqrMagnitude < 0.001f;
            return matchPosition && matchUp && matchScale && matchColor;
        }

        public override int GetHashCode()
        {
            int hashCode = -1285106862;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + scale.GetHashCode();
            return hashCode;
        }

        public static bool IsHandle(SplineMode mode, int index)
        {
            var isHandle = mode == SplineMode.Bezier && index % 3 != 0;
            return isHandle;
        }

        public static int GetAnchorIndex(SplineMode mode, int index)
        {
            if (mode != SplineMode.Bezier)
            {
                return index;
            }
            else
            {
                var index_mod = index % 3;

                if (index_mod == 1) return index - 1;
                else if (index_mod == 2) return index + 1;

                return index;
            }
        }

        public static void GetHandleIndexes(SplineMode mode, bool isClosed, int pointCount, int index, out int handleIndex0, out int handleIndex1)
        {
            if (mode != SplineMode.Bezier)
            {
                handleIndex0 = index;
                handleIndex1 = index;
            }
            else
            {
                var anchorIndex = GetAnchorIndex(mode, index);
                handleIndex0 = anchorIndex - 1;
                handleIndex1 = anchorIndex + 1;

                if (isClosed)
                {
                    if (handleIndex0 == -1) handleIndex0 = pointCount - 1;
                    if (handleIndex1 == pointCount) handleIndex1 = 1;
                }
            }
        }
    }
}