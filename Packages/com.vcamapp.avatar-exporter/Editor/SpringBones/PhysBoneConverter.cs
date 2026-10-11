using System.Collections.Generic;
using System.Linq;
using UniGLTF.SpringBoneJobs;
using UniVRM10;
using UnityEngine;
using VRC.Dynamics;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Approximates PhysBones with VRM spring bones on the export copy. The two solvers differ, so this keeps the
    /// overall feel (how stiff, how heavy, what it collides with) rather than matching the motion. With the curve
    /// values at each bone's depth, pull p, spring (momentum when advanced) s, gravity g and gravity falloff f become
    /// stiffness 4p, drag max(0.6 (1 - s), 0.2) and gravity power |g| (1 - f), scaled by 4p for PhysBone 1.1 where
    /// gravity works against pull. Immobile, which damps the motion of what the bones are measured from, goes to
    /// <c>VCAM_springBone_immobile</c> (docs/springbone-immobile.md). A VRM spring's center takes out all of that motion,
    /// so for readers without the extension a chain at least half immobile is measured in that node.
    /// Every position, direction and radius is written in the frame the bones have after the export normalizes
    /// them (world axes at unit scale), because UniVRM's normalization converts collider offsets and radii but leaves
    /// capsule tails and plane normals in the old bone frame.
    /// </summary>
    internal sealed class PhysBoneConverter
    {
        /// <summary>
        /// VRM's drag is all that damps a swing, so with less a chain of high spring keeps swinging for seconds after
        /// the avatar stops, where this settles it in under a second.
        /// </summary>
        private const float MinimumDrag = 0.2f;

        private readonly GameObject _root;
        private readonly ExportReport _report;
        private readonly Dictionary<VRCPhysBoneColliderBase, VRM10SpringBoneCollider> _colliders = new();
        private readonly List<ImmobileJoint> _immobileJoints = new();

        private PhysBoneConverter(GameObject root, ExportReport report)
        {
            _root = root;
            _report = report;
        }

        internal static (Vrm10InstanceSpringBone SpringBone, IReadOnlyList<ImmobileJoint> ImmobileJoints) Convert(GameObject root, ExportReport report) =>
            new PhysBoneConverter(root, report).Convert();

        private (Vrm10InstanceSpringBone, IReadOnlyList<ImmobileJoint>) Convert()
        {
            var springBone = new Vrm10InstanceSpringBone();
            foreach (var physBone in _root.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                if (!physBone.enabled || !physBone.gameObject.activeInHierarchy)
                {
                    continue;
                }
                ConvertPhysBone(physBone, springBone);
            }
            foreach (var spring in springBone.Springs)
            {
                spring.Center = AboveJoints(spring.Center);
            }
            var immobileJoints = _immobileJoints
                .Select(joint => (joint, Reference: AboveJoints(joint.Reference)))
                .Where(pair => pair.Reference != null)
                .Select(pair => new ImmobileJoint(pair.joint.Joint, pair.Reference, pair.joint.Immobile))
                .ToList();
            return (springBone, immobileJoints);
        }

        private void ConvertPhysBone(VRCPhysBoneBase physBone, Vrm10InstanceSpringBone springBone)
        {
            var root = physBone.GetRootTransform();
            var ignored = new HashSet<Transform>(physBone.ignoreTransforms.Where(t => t != null));
            var chains = new List<List<Transform>>();
            var startsAtRoot = root.childCount <= 1 || physBone.multiChildType != VRCPhysBoneBase.MultiChildType.Ignore;
            if (startsAtRoot)
            {
                CollectChains(root, new List<Transform>(), chains, ignored);
            }
            else
            {
                foreach (Transform child in root)
                {
                    if (!ignored.Contains(child))
                    {
                        CollectChains(child, new List<Transform>(), chains, ignored);
                    }
                }
            }

            var maxDepth = Mathf.Max(1, chains.Select(chain => Depth(root, chain[^1])).DefaultIfEmpty(0).Max());
            var colliderGroup = ColliderGroup(physBone, springBone);
            foreach (var chain in chains)
            {
                AppendEndpoint(physBone, chain);
                if (chain.Count < 2)
                {
                    continue;
                }
                if (chain.Any(t => t.GetComponent<VRM10SpringBoneJoint>() != null))
                {
                    _report.Add("Report:PhysBoneOverlap", physBone.name, chain[0].name);
                    continue;
                }

                var spring = new Vrm10InstanceSpringBone.Spring(physBone.name);
                var reference = ImmobileReference(physBone, root, chain[0]);
                var immobile = new float[chain.Count - 1];
                for (var index = 0; index < chain.Count; index++)
                {
                    var depth = (float)Depth(root, chain[index]) / maxDepth;
                    var tail = index + 1 < chain.Count ? chain[index + 1] : null;
                    spring.Joints.Add(CreateJoint(physBone, chain[index], tail, depth));
                    if (tail != null)
                    {
                        immobile[index] = Mathf.Clamp01(Evaluate(physBone.immobile, physBone.immobileCurve, depth));
                    }
                }
                if (reference != null && immobile.Any(value => value > 0))
                {
                    spring.Center = immobile[0] >= 0.5f ? reference : null;
                    _immobileJoints.AddRange(immobile.Select((value, index) => new ImmobileJoint(chain[index], reference, value)));
                }
                if (colliderGroup != null)
                {
                    spring.ColliderGroups.Add(colliderGroup);
                }
                springBone.Springs.Add(spring);
            }
        }

        /// <summary>
        /// What Immobile measures the motion of the bones from: the root's parent, or for World the avatar, which
        /// moves only with what is right below the avatar root. Null for the avatar root itself, which UniVRM writes as
        /// the scene rather than a node.
        /// </summary>
        private Transform ImmobileReference(VRCPhysBoneBase physBone, Transform root, Transform first)
        {
            var reference = physBone.immobileType == VRCPhysBoneBase.ImmobileType.World ? TopAncestor(first) : root.parent;
            return reference == null || reference == _root.transform || reference == first ? null : reference;
        }

        /// <summary>The ancestor of `bone` right below the avatar root.</summary>
        private Transform TopAncestor(Transform bone)
        {
            while (bone.parent != null && bone.parent != _root.transform)
            {
                bone = bone.parent;
            }
            return bone;
        }

        /// <summary>
        /// `node`, or the node above the highest joint at or above it, as for a PhysBone nested in another: VRM
        /// refuses to measure a spring in a node another spring swings. Null for the avatar root.
        /// </summary>
        private Transform AboveJoints(Transform node)
        {
            if (node == null)
            {
                return null;
            }
            var lifted = node;
            for (var current = node; current != _root.transform; current = current.parent)
            {
                if (current.GetComponent<VRM10SpringBoneJoint>() != null)
                {
                    lifted = current.parent;
                }
            }
            return lifted == _root.transform ? null : lifted;
        }

        /// <summary>
        /// Splits the bone tree into chains. A branch continues into its first child, and every other child starts
        /// a chain of its own, so no bone is moved by two springs.
        /// </summary>
        private static void CollectChains(Transform bone, List<Transform> chain, List<List<Transform>> chains, HashSet<Transform> ignored)
        {
            chain.Add(bone);
            var children = bone.Cast<Transform>().Where(child => !ignored.Contains(child)).ToList();
            if (children.Count == 0)
            {
                chains.Add(chain);
                return;
            }
            CollectChains(children[0], chain, chains, ignored);
            foreach (var child in children.Skip(1))
            {
                CollectChains(child, new List<Transform>(), chains, ignored);
            }
        }

        /// <summary>A PhysBone endpoint gives the last bone a length to swing; VRM needs a real node at the tip.</summary>
        private static void AppendEndpoint(VRCPhysBoneBase physBone, List<Transform> chain)
        {
            if (physBone.endpointPosition == Vector3.zero)
            {
                return;
            }
            var last = chain[^1];
            var endpoint = new GameObject(last.name + "_end").transform;
            endpoint.SetParent(last, false);
            endpoint.localPosition = physBone.endpointPosition;
            chain.Add(endpoint);
        }

        /// <param name="tail">The next bone of the chain, which the joint swings; null for the last, which is only a tail.</param>
        private static VRM10SpringBoneJoint CreateJoint(VRCPhysBoneBase physBone, Transform bone, Transform tail, float depth)
        {
            // Values out of the ranges PhysBone's inspector offers turn up in avatars (a negative radius, say), and
            // VRM refuses negative radii and stiffness.
            var pull = Mathf.Clamp01(Evaluate(physBone.pull, physBone.pullCurve, depth));
            var spring = Mathf.Clamp01(Evaluate(physBone.spring, physBone.springCurve, depth));
            var gravity = Evaluate(physBone.gravity, physBone.gravityCurve, depth);
            var falloff = Mathf.Clamp01(Evaluate(physBone.gravityFalloff, physBone.gravityFalloffCurve, depth));

            var gravityPower = Mathf.Abs(gravity) * (1 - falloff);
            if (physBone.version != VRCPhysBoneBase.Version.Version_1_0)
            {
                gravityPower *= 4 * pull;
            }

            var joint = bone.gameObject.AddComponent<VRM10SpringBoneJoint>();
            joint.m_stiffnessForce = 4 * pull;
            joint.m_dragForce = Mathf.Max(0.6f * (1 - spring), MinimumDrag);
            joint.m_gravityPower = gravityPower;
            joint.m_gravityDir = gravity >= 0 ? Vector3.down : Vector3.up;
            joint.m_jointRadius = Mathf.Max(0, Evaluate(physBone.radius, physBone.radiusCurve, depth)) * Scale(bone);
            if (tail != null)
            {
                ApplyLimit(joint, physBone, bone, tail.position - bone.position, depth);
            }
            return joint;
        }

        /// <summary>
        /// Maps the PhysBone angle limit to the VRM one (`VRMC_springBone_limit`): Angle to a cone, Hinge to a hinge
        /// and Polar to a spherical limit, with Max Angle X as the angle or pitch and Max Angle Z as the yaw.
        /// Both orient a limit as the shortest turn from +Y to the bone, then a rotation, but PhysBone turns in the
        /// bone's own frame with an X, Y, Z Euler rotation, while the VRM limit turns in the frame the export
        /// normalizes the bone to. The rotation is re-expressed so the limit stays where it is in the world.
        /// </summary>
        private static void ApplyLimit(VRM10SpringBoneJoint joint, VRCPhysBoneBase physBone, Transform bone, Vector3 boneVector, float depth)
        {
            if (physBone.limitType == VRCPhysBoneBase.LimitType.None || boneVector.sqrMagnitude < 1e-12f)
            {
                return;
            }
            var rotation = new Vector3(
                Evaluate(physBone.limitRotation.x, physBone.limitRotationXCurve, depth),
                Evaluate(physBone.limitRotation.y, physBone.limitRotationYCurve, depth),
                Evaluate(physBone.limitRotation.z, physBone.limitRotationZCurve, depth));
            var euler = Quaternion.AngleAxis(rotation.z, Vector3.forward)
                * Quaternion.AngleAxis(rotation.y, Vector3.up)
                * Quaternion.AngleAxis(rotation.x, Vector3.right);
            var worldLimit = bone.rotation * Quaternion.FromToRotation(Vector3.up, bone.InverseTransformDirection(boneVector)) * euler;
            joint.m_limitSpaceOffset = Quaternion.Inverse(DefaultLimitOrientation(boneVector.normalized)) * worldLimit;

            var angleX = Mathf.Clamp(Evaluate(physBone.maxAngleX, physBone.maxAngleXCurve, depth), 0, 180) * Mathf.Deg2Rad;
            var angleZ = Mathf.Clamp(Evaluate(physBone.maxAngleZ, physBone.maxAngleZCurve, depth), 0, 90) * Mathf.Deg2Rad;
            joint.m_pitch = angleX;
            joint.m_yaw = angleZ;
            joint.m_anglelimitType = physBone.limitType switch
            {
                VRCPhysBoneBase.LimitType.Angle => AnglelimitTypes.Cone,
                VRCPhysBoneBase.LimitType.Hinge => AnglelimitTypes.Hinge,
                _ => AnglelimitTypes.Spherical,
            };
        }

        /// <summary>
        /// Where `VRMC_springBone_limit` puts a limit before its rotation, for a bone pointing along `direction` with no
        /// rotation of its own: the shortest turn from +Y, and a half turn about X for a bone pointing straight down.
        /// </summary>
        internal static Quaternion DefaultLimitOrientation(Vector3 direction) =>
            direction.y <= -1 + 1e-6f
                ? new Quaternion(1, 0, 0, 0)
                : new Quaternion(direction.z, 0, -direction.x, direction.y + 1).normalized;

        private VRM10SpringBoneColliderGroup ColliderGroup(VRCPhysBoneBase physBone, Vrm10InstanceSpringBone springBone)
        {
            var colliders = physBone.colliders.Where(collider => collider != null).Select(Collider).Where(collider => collider != null).ToList();
            if (colliders.Count == 0)
            {
                return null;
            }
            var group = _root.AddComponent<VRM10SpringBoneColliderGroup>();
            group.Name = physBone.name;
            group.Colliders = colliders;
            springBone.ColliderGroups.Add(group);
            return group;
        }

        private VRM10SpringBoneCollider Collider(VRCPhysBoneColliderBase source)
        {
            if (_colliders.TryGetValue(source, out var converted))
            {
                return converted;
            }

            var transform = source.rootTransform != null ? source.rootTransform : source.transform;
            if (transform == _root.transform)
            {
                // UniVRM writes the root as the scene rather than a node, so it drops colliders on the root, and the
                // collider groups then point at the colliders after them.
                var holder = new GameObject(source.name).transform;
                holder.SetParent(transform, false);
                transform = holder;
            }
            var collider = transform.gameObject.AddComponent<VRM10SpringBoneCollider>();
            Vector3 Offset(Vector3 local) => transform.TransformPoint(local) - transform.position;
            collider.Radius = Mathf.Max(0, source.radius) * Scale(transform);
            collider.Offset = Offset(source.position);
            var axis = source.rotation * Vector3.up;
            switch (source.shapeType)
            {
                case VRCPhysBoneColliderBase.ShapeType.Sphere:
                    collider.ColliderType = source.insideBounds ? VRM10SpringBoneColliderTypes.SphereInside : VRM10SpringBoneColliderTypes.Sphere;
                    break;
                case VRCPhysBoneColliderBase.ShapeType.Capsule:
                    // PhysBone capsule height includes both caps; VRM capsules are given by the centers of the caps.
                    var half = Mathf.Max(0, source.height / 2 - source.radius);
                    collider.ColliderType = source.insideBounds ? VRM10SpringBoneColliderTypes.CapsuleInside : VRM10SpringBoneColliderTypes.Capsule;
                    collider.Offset = Offset(source.position - axis * half);
                    collider.Tail = Offset(source.position + axis * half);
                    break;
                case VRCPhysBoneColliderBase.ShapeType.Plane:
                    collider.ColliderType = VRM10SpringBoneColliderTypes.Plane;
                    collider.Normal = transform.TransformDirection(axis).normalized;
                    break;
                default:
                    Object.DestroyImmediate(collider);
                    _report.Add("Report:UnsupportedCollider", source.name, source.shapeType.ToString());
                    collider = null;
                    break;
            }
            _colliders[source] = collider;
            return collider;
        }

        /// <summary>PhysBone sizes scale with the bone, like the uniform scale UniVRM uses for its colliders.</summary>
        private static float Scale(Transform transform)
        {
            var scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        private static float Evaluate(float value, AnimationCurve curve, float depth) =>
            curve != null && curve.length > 0 ? value * curve.Evaluate(depth) : value;

        private static int Depth(Transform root, Transform bone)
        {
            var depth = 0;
            for (var current = bone; current != null && current != root; current = current.parent)
            {
                depth++;
            }
            return depth;
        }
    }
}
