#if !UNITY_2019_3_OR_NEWER
#define CINEMACHINE_PHYSICS
#endif

using UnityEngine;
using Cinemachine.Utility;

namespace Cinemachine
{
#if CINEMACHINE_PHYSICS
    /// <summary>
    /// Third-person follower, with complex pivoting: horizontal about the origin, 
    /// vertical about the shoulder.  
    /// </summary>
    [AddComponentMenu("")] // Don't display in add component menu
    [SaveDuringPlay]
    public class Cinemachine3rdPersonFollow : CinemachineComponentBase
    {
        /// <summary>How responsively the camera tracks the target.  Each axis (camera-local) 
        /// can have its own setting.  Value is the approximate time it takes the camera 
        /// to catch up to the target's new position.  Smaller values give a more rigid 
        /// effect, larger values give a squishier one.</summary>
        [Tooltip("How responsively the camera tracks the target.  Each axis (camera-local) "
           + "can have its own setting.  Value is the approximate time it takes the camera "
           + "to catch up to the target's new position.  Smaller values give a more "
           + "rigid effect, larger values give a squishier one")]
        public Vector3 Damping;

        /// <summary>Position of the shoulder pivot relative to the Follow target origin.  
        /// This offset is in target-local space.</summary>
        [Header("Rig")]
        [Tooltip("Position of the shoulder pivot relative to the Follow target origin.  "
            + "This offset is in target-local space")]
        public Vector3 ShoulderOffset;

        /// <summary>Vertical offset of the hand in relation to the shoulder.  
        /// Arm length will affect the follow target's screen position 
        /// when the camera rotates vertically.</summary>
        [Tooltip("Vertical offset of the hand in relation to the shoulder.  "
            + "Arm length will affect the follow target's screen position when "
            + "the camera rotates vertically")]
        public float VerticalArmLength;

        /// <summary>Specifies which shoulder (left, right, or in-between) the camera is on.</summary>
        [Tooltip("Specifies which shoulder (left, right, or in-between) the camera is on")]
        [Range(0, 1)]
        public float CameraSide;

        /// <summary>How far baehind the hand the camera will be placed.</summary>
        [Tooltip("How far baehind the hand the camera will be placed")]
        public float CameraDistance;

        /// <summary>Camera will avoid obstacles on these layers.</summary>
        [Header("Obstacles")]
        [Tooltip("Camera will avoid obstacles on these layers")]
        public LayerMask CameraCollisionFilter;

        /// <summary>
        /// Obstacles with this tag will be ignored.  It is a good idea 
        /// to set this field to the target's tag
        /// </summary>
        [TagField]
        [Tooltip("Obstacles with this tag will be ignored.  "
            + "It is a good idea to set this field to the target's tag")]
        public string IgnoreTag = string.Empty;

        /// <summary>
        /// Specifies how close the camera can get to obstacles
        /// </summary>
        [Tooltip("Specifies how close the camera can get to obstacles")]
        public float CameraRadius;
        
        /// <summary>
        /// How gradually the camera returns to its normal position after having been corrected by the built-in
        /// collision resolution system. Higher numbers will move the camera more gradually back to normal.
        /// </summary>
        [Range(0, 10)]
        [Tooltip("How gradually the camera returns to its normal position after having been corrected by the built-in " +
            "collision resolution system.  Higher numbers will move the camera more gradually back to normal.")]
        public float PostCorrectionDamping = 0;

        // State info
        Vector3 m_PreviousFollowTargetPosition;
        float m_PreviousHeadingAngle;
        float m_PrevHandDistance;
        float m_PrevCamPosDistance;

        void OnValidate()
        {
            CameraSide = Mathf.Clamp(CameraSide, -1.0f, 1.0f);
            Damping.x = Mathf.Max(0, Damping.x);
            Damping.y = Mathf.Max(0, Damping.y);
            Damping.z = Mathf.Max(0, Damping.z);
            CameraRadius = Mathf.Max(0.001f, CameraRadius);
        }

        void Reset()
        {
            CameraCollisionFilter = 1;
            ShoulderOffset = new Vector3(0.5f, -0.4f, 0.0f);
            VerticalArmLength = 0.4f;
            CameraSide = 1.0f;
            CameraDistance = 2.0f;
            Damping = new Vector3(0.1f, 0.5f, 0.3f);
            CameraRadius = 0.2f;
        }

        /// <summary>True if component is enabled and has a Follow target defined</summary>
        public override bool IsValid => enabled && FollowTarget != null;

        /// <summary>Get the Cinemachine Pipeline stage that this component implements.
        /// Always returns the Aim stage</summary>
        public override CinemachineCore.Stage Stage { get { return CinemachineCore.Stage.Body; } }

        /// <summary>
        /// Report maximum damping time needed for this component.
        /// </summary>
        /// <returns>Highest damping setting in this component</returns>
        public override float GetMaxDampTime() { return Mathf.Max(Damping.x, Mathf.Max(Damping.y, Damping.z)); }

        /// <summary>Orients the camera to match the Follow target's orientation</summary>
        /// <param name="curState">The current camera state</param>
        /// <param name="deltaTime">Not used.</param>
        public override void MutateCameraState(ref CameraState curState, float deltaTime)
        {
            if (!IsValid)
                return;
            if (!VirtualCamera.PreviousStateIsValid)
                deltaTime = -1;
            PositionCamera(ref curState, deltaTime);
        }

        void PositionCamera(ref CameraState curState, float deltaTime)
        {
            var prevTargetPos = deltaTime >= 0 ? m_PreviousFollowTargetPosition : FollowTargetPosition;

            // Compute damped target pos (compute in camera space)
            var dampedTargetPos = Quaternion.Inverse(curState.RawOrientation) 
                * (FollowTargetPosition - prevTargetPos);
            if (deltaTime >= 0)
                dampedTargetPos = VirtualCamera.DetachedFollowTargetDamp(
                    dampedTargetPos, Damping, deltaTime);
            dampedTargetPos = prevTargetPos + curState.RawOrientation * dampedTargetPos;

            // Get target rotation (worldspace)
            var fwd = Vector3.forward;
            var up = Vector3.up;
            var followTargetRotation = FollowTargetRotation;
            var followTargetForward = followTargetRotation * fwd;
            var angle = UnityVectorExtensions.SignedAngle(
                fwd, followTargetForward.ProjectOntoPlane(up), up);
            var previousHeadingAngle = deltaTime >= 0 ? m_PreviousHeadingAngle : angle;
            var deltaHeading = angle - previousHeadingAngle;
            m_PreviousHeadingAngle = angle;

            // Bypass user-sourced rotation
            dampedTargetPos = FollowTargetPosition 
                + Quaternion.AngleAxis(deltaHeading, up) * (dampedTargetPos - FollowTargetPosition);
            m_PreviousFollowTargetPosition = dampedTargetPos;

            GetRigPositions(out Vector3 root, out _, out Vector3 hand);

            // 1. Check if pivot itself is colliding with something, if yes, then move the pivot 
            // closer to the player. The radius is bigger here than in step 2, to avoid problems 
            // next to walls. Where the preferred distance would be pulled completely to the 
            // player, using a bigger radius, this won't happen.
            bool handHasHit = PullTowardsStartOnCollision(in root, in hand, in CameraCollisionFilter, 
                CameraRadius * 1.05f, out var handResolved);
            // Post correction damping
            Vector3 dampedHandResolved = DampedPullBackPostCollision(handHasHit, deltaTime, 
                root, handResolved, ref m_PrevHandDistance);
            
            // 2. Try to place the camera to the preferred distance
            Vector3 camPos = dampedHandResolved - (followTargetForward * CameraDistance);
            bool camPosHasHit = PullTowardsStartOnCollision(in dampedHandResolved, in camPos, in CameraCollisionFilter, 
                CameraRadius, out var camPosResolved);
            // Post correction damping
            Vector3 dampedCamPosResolved = DampedPullBackPostCollision(camPosHasHit, deltaTime, 
                    root, camPosResolved, ref m_PrevCamPosDistance);
            
            // Set state
            curState.RawPosition = dampedCamPosResolved;
            curState.RawOrientation = FollowTargetRotation;
            curState.ReferenceUp = up;
        }

        /// <summary>
        /// Internal use only.  Public for the inspector gizmo
        /// </summary>
        /// <param name="root">Root of the rig.</param>
        /// <param name="shoulder">Shoulder of the rig.</param>
        /// <param name="hand">Hand of the rig.</param>
        public void GetRigPositions(out Vector3 root, out Vector3 shoulder, out Vector3 hand)
        {
            root = m_PreviousFollowTargetPosition;
            var shoulderPivotReflected = Vector3.Reflect(ShoulderOffset, Vector3.right);
            var shoulderOffset = Vector3.Lerp(shoulderPivotReflected, ShoulderOffset, CameraSide);
            t_HandOffset.y = VerticalArmLength;
            shoulder = root + Quaternion.AngleAxis(m_PreviousHeadingAngle, Vector3.up) * shoulderOffset;
            hand = shoulder + FollowTargetRotation * t_HandOffset;
        }
        Vector3 t_HandOffset = Vector3.zero; // minor opt. - to avoid creating a new vector in GetRigPositions

        bool PullTowardsStartOnCollision(
            in Vector3 rayStart, in Vector3 rayEnd,
            in LayerMask filter, float radius,
            out Vector3 result)
        {
            var dir = rayEnd - rayStart;
            bool hasHit = RuntimeUtility.SphereCastIgnoreTag(
                rayStart, radius, dir, out RaycastHit hitInfo, dir.magnitude, filter, IgnoreTag);
            result = hasHit ? hitInfo.point + hitInfo.normal * radius: rayEnd;
            return hasHit;
        }
        
        
        /// <summary>
        /// Pulls camera back to its normal position when not colliding. The pull back speed is determined by
        /// the PostCorrectionDamping class member and deltaTime.
        /// </summary>
        /// <param name="isColliding">Is the camera currently colliding?</param>
        /// <param name="deltaTime">Used for damping.</param>
        /// <param name="root">Root of the rig</param>
        /// <param name="resolved">Position of the camera.</param>
        /// <param name="previousDistance">Distance of the camera from root in the previous frame.</param>
        /// <returns>Damped position</returns>
        Vector3 DampedPullBackPostCollision(bool isColliding, float deltaTime, 
            Vector3 root, Vector3 resolved, ref float previousDistance)
        {
            // Post correction damping without rotational damp
            var dampedResolved = resolved;
            if (!isColliding && PostCorrectionDamping > 0 && deltaTime >= 0)
            {
                Vector3 difference = resolved - root;
                float delta = difference.magnitude - previousDistance;
                delta = Damper.Damp(delta, PostCorrectionDamping, deltaTime);
                dampedResolved = root + difference.normalized * (delta + previousDistance);
            }
            previousDistance = (dampedResolved - root).magnitude;

            return dampedResolved;
        }
    }
#endif
}
