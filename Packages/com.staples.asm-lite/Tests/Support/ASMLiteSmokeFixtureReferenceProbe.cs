using System;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    public sealed class ASMLiteSmokeFixtureReferenceProbe : MonoBehaviour
    {
        public int number;
        public string text;
        public UnityEngine.Object internalReference;
        public UnityEngine.Object externalReference;
        [SerializeReference] public Graph data;

        [Serializable]
        public sealed class Graph
        {
            public int value;
            public UnityEngine.Object reference;
            [SerializeReference] public Graph next;
        }
    }
}
