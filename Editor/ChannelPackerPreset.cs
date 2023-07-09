#if UNITY_EDITOR
using UnityEngine;

namespace ChannelPacker {
	public class ChannelPackerPreset : ScriptableObject {
		public string[] names = new string[4] { "Red", "Green", "Blue", "Alpha" };
		public float[] defaults = new float[4];
		public ChannelPacker.ColorChannel[] froms = new ChannelPacker.ColorChannel[4];
		public bool[] inverts = new bool[4];

		public string previewShader;
		public string previewMapKeyword;
	}
}
#endif