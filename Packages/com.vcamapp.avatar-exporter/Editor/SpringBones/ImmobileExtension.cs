using System;
using System.Collections.Generic;
using UniGLTF;
using UniJSON;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>A joint of a PhysBone with Immobile, and the node Immobile measures from.</summary>
    internal readonly struct ImmobileJoint
    {
        internal readonly Transform Joint;
        internal readonly Transform Reference;
        internal readonly float Immobile;

        internal ImmobileJoint(Transform joint, Transform reference, float immobile)
        {
            Joint = joint;
            Reference = reference;
            Immobile = immobile;
        }
    }

    /// <summary>Writes the <c>VCAM_springBone_immobile</c> extension described in docs/springbone-immobile.md.</summary>
    internal static class ImmobileExtension
    {
        internal const string Name = "VCAM_springBone_immobile";
        internal const string SpecVersion = "1.0";

        /// <param name="nodeIndex">The glTF node a transform is written as.</param>
        internal static ArraySegment<byte> Root(IReadOnlyList<ImmobileJoint> joints, Func<Transform, int> nodeIndex)
        {
            var json = new JsonFormatter();
            json.BeginMap();
            json.Key("specVersion");
            json.Value(SpecVersion);
            json.Key("joints");
            json.BeginList();
            foreach (var joint in joints)
            {
                json.BeginMap();
                json.Key("node");
                json.Value(nodeIndex(joint.Joint));
                json.Key("reference");
                json.Value(nodeIndex(joint.Reference));
                json.Key("immobile");
                json.Value(joint.Immobile);
                json.EndMap();
            }
            json.EndList();
            json.EndMap();
            return json.GetStoreBytes();
        }
    }
}
