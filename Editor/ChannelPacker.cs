#if UNITY_EDITOR
/*
Channel Packer
Made by Camobiwon

Original code from: https://www.reddit.com/r/Unity3D/comments/glkvp2/i_made_another_mask_map_packer_for_hdrp/
Thank you original creator! This has been extremely useful to me, and whoever is using this, I hope Channel Packer is useful to you :)
*/

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Windows;

namespace ChannelPacker {
	public class ChannelPacker : EditorWindow {
		//Use a compute shader to greatly speed up packing time
		[SerializeField]
		private ComputeShader fastPack;

		private static ChannelPacker window;
		public ChannelPackerPreset preset;
		public ChannelPackerSettings settings;

		//Inputs
		private Texture2D[] inputs = new Texture2D[4];
		public float[] defaults = new float[4];
		public float[] mults = new float[4] { 1, 1, 1, 1 };
		public ColorChannel[] froms = new ColorChannel[4];
		public bool[] inverts = new bool[4];
		
		private Texture2D previewAlbedo, previewNormal;

		private RenderTexture[] blits = new RenderTexture[4];

		private Vector2 scrollPos;
		private GUIStyle regularStyle, regularSmall, smallWarn, regularWarn;
		private RenderTexture packedTexture;
		private Texture2D finalTexture;
		private Vector2Int textureDimensions;
		private Editor previewMatViewer;
		private Material previewMat;
		private bool previewShaderFound;

		//Show the window
		[MenuItem("Tools/Channel Packer")]
		public static void ShowWindow() {
			window = (ChannelPacker)GetWindow(typeof(ChannelPacker), false, "Channel Packer");
		}

		private void OnEnable() {
			LoadSettings();
			InitGUIStyles();
			textureDimensions = Vector2Int.zero;
		}

		//If for some reason the window becomes null, get it again.
		private void OnInspectorUpdate() {
			if(!window)
				window = (ChannelPacker)GetWindow(typeof(ChannelPacker), false, "Channel Packer");
		}

		private void OnGUI() {
			if(window) {
				window.Repaint();
				GUILayout.BeginArea(new Rect(0, 0, window.position.size.x, window.position.size.y));
				GUILayout.BeginVertical();
				scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.ExpandHeight(true));
			}

			if(!inputs[0] && !inputs[1] && !inputs[2] && !inputs[3])
				textureDimensions = Vector2Int.zero;

			GUILayout.Label("Channel Packer", regularStyle);
			GUILayout.Label("Add textures to be packed together", regularStyle);

			//Inputs
			ChannelInput(0); //Red
			ChannelInput(1); //Green
			ChannelInput(2); //Blue
			ChannelInput(3); //Alpha

			//Input field for each color channel
			void ChannelInput(int channelInput) {
				GUILayout.BeginVertical(EditorStyles.helpBox);
				inputs[channelInput] = (Texture2D)EditorGUILayout.ObjectField($"{preset.names[channelInput]} Input", inputs[channelInput], typeof(Texture2D), false);
				if(!inputs[channelInput]) {
					GUILayout.Label($"No {preset.names[channelInput]} Input, use slider to set value", regularSmall);
					defaults[channelInput] = EditorGUILayout.Slider(defaults[channelInput], 0f, 1f);

				} else {
					if(textureDimensions != Vector2Int.zero && (inputs[channelInput].width != textureDimensions.x || inputs[channelInput].height != textureDimensions.y)) {
						inputs[channelInput] = null;
						Debug.LogWarning("Input texture is not the same resolution as other textures! Rejecting");
					}
					if(textureDimensions == Vector2Int.zero) {
						textureDimensions.x = inputs[channelInput].width;
						textureDimensions.y = inputs[channelInput].height;
					}

					froms[channelInput] = (ColorChannel)EditorGUILayout.EnumPopup("From Channel", froms[channelInput]);
					mults[channelInput] = EditorGUILayout.Slider($"Multiplier", mults[channelInput], 0f, 1f);
					inverts[channelInput] = EditorGUILayout.Toggle("Invert", inverts[channelInput]);
					if(inputs[channelInput] && inputs[channelInput].graphicsFormat.ToString().Contains("SRGB"))
						GUILayout.Label("Texture marked as sRGB! Disabling recommended", smallWarn);
				}
				GUILayout.EndVertical();
			}

			GUILayout.Space(5f);

			//Main Options
			GUILayout.BeginVertical(EditorStyles.helpBox);
			if(GUILayout.Button("Pack Texture") && textureDimensions != Vector2Int.zero) {
				CreatePackedTexture();
				SaveTexture();
				EditorUtility.ClearProgressBar();
			}
			if(GUILayout.Button("Clear All")) {
				inputs[0] = inputs[1] = inputs[2] = inputs[3] = previewAlbedo = previewNormal = null;
				previewMatViewer = null;
			}
			if(GUILayout.Button("Save Preset")) {
				SavePreset();
			}

			EditorGUI.BeginChangeCheck();
			preset = (ChannelPackerPreset)EditorGUILayout.ObjectField(new GUIContent("Preset", "The preset packing settings to be used"), preset, typeof(ChannelPackerPreset), preset);
			if(EditorGUI.EndChangeCheck()) {
				LoadSettings();
			}

			GUILayout.EndVertical();
			GUILayout.Space(5f);
			GUILayout.BeginVertical(EditorStyles.helpBox);

			//Preview
			if(previewShaderFound) {
				if(GUILayout.Button("Update Preview") && textureDimensions != Vector2Int.zero) {
					EditorUtility.DisplayProgressBar("Packing texture", "", 0f);
					CreatePackedTexture();
					EditorUtility.ClearProgressBar();
				}

				previewAlbedo = (Texture2D)EditorGUILayout.ObjectField("Preview Albedo (Optional)", previewAlbedo, typeof(Texture2D), false);
				previewNormal = (Texture2D)EditorGUILayout.ObjectField("Preview Normal (Optional)", previewNormal, typeof(Texture2D), false);

				if(previewMat != null && previewMatViewer != null) {
					GUILayout.Label("Preview", regularStyle);
					previewMatViewer.OnPreviewGUI(GUILayoutUtility.GetRect(256, 256), EditorStyles.objectField);
					GUILayout.Space(10f);
				}
			} else if(string.IsNullOrEmpty(preset.previewShader)) {
				GUILayout.Label($"Preset does not have a path for a preview shader. Assign a shader path and map keyword in the preset file", regularWarn);
			} else {
				GUILayout.Label($"Preview Shader ({preset.previewShader}) not found.\nYou can still compile maps, but previewing is disabled.", regularWarn);
			}

			GUILayout.EndVertical();

			if(window) {
				GUILayout.EndScrollView();
				GUILayout.EndVertical();
				GUILayout.EndArea();
			}
		}

		private void CreatePackedTexture() {
			finalTexture = new Texture2D(textureDimensions.x, textureDimensions.y, TextureFormat.ARGB32, false, true);
			int blitKernel = fastPack.FindKernel("ChannelSet");

			PackTexture(0); //Red
			PackTexture(1); //Green
			PackTexture(2); //Blue
			PackTexture(3); //Alpha

			//Prepare textures for packing
			void PackTexture(int channelInput) {
				EditorUtility.DisplayProgressBar($"Packing {preset.names[channelInput]}", "", 1f);
				blits[channelInput] = new RenderTexture(textureDimensions.x, textureDimensions.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				if(inputs[channelInput])
					Graphics.Blit(inputs[channelInput], blits[channelInput]);
				else {
					blits[channelInput].enableRandomWrite = true;
					blits[channelInput].Create();

					fastPack.SetTexture(blitKernel, "Packed", blits[channelInput]);
					fastPack.SetFloat("packedCol", defaults[channelInput]);
					fastPack.Dispatch(blitKernel, textureDimensions.x, textureDimensions.y, 1);
				}
			}

			EditorUtility.DisplayProgressBar("Combining Maps", "", 1f);
			//Create the render texture
			if(textureDimensions != Vector2Int.zero) {
				//Setup
				packedTexture = new RenderTexture(textureDimensions.x, textureDimensions.y, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				packedTexture.enableRandomWrite = true;
				packedTexture.Create();

				//Send textures to compute shader
				int kernel = fastPack.FindKernel("CSMain");
				fastPack.SetTexture(kernel, "Result", packedTexture);
				fastPack.SetTexture(kernel, "r", blits[0]);
				fastPack.SetTexture(kernel, "g", blits[1]);
				fastPack.SetTexture(kernel, "b", blits[2]);
				fastPack.SetTexture(kernel, "a", blits[3]);

				//Ternary hell, send data to compute shader for processing
				fastPack.SetInts("froms", inputs[0] ? (int)froms[0] : 0, inputs[1] ? (int)froms[1] : 0, inputs[2] ? (int)froms[2] : 0, inputs[3] ? (int)froms[3] : 0);
				fastPack.SetInts("inverts", inputs[0] ? (inverts[0] ? 1 : 0) : 0, inputs[1] ? (inverts[1] ? 1 : 0) : 0, inputs[2] ? (inverts[2] ? 1 : 0) : 0, inputs[3] ? (inverts[3] ? 1 : 0) : 0);
				fastPack.SetFloats("mults", inputs[0] ? mults[0] : 1, inputs[1] ? mults[1] : 1, inputs[2] ? mults[2] : 1, inputs[3] ? mults[3] : 1);
				fastPack.Dispatch(kernel, textureDimensions.x, textureDimensions.y, 1);

				//Final output
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = packedTexture;
				finalTexture.ReadPixels(new Rect(0, 0, packedTexture.width, packedTexture.height), 0, 0);
				finalTexture.Apply();
				RenderTexture.active = previous;
			}

			if(previewShaderFound) {
				GeneratePreview();
			}
		}

		private void SaveTexture() {
			//Find non-null channel input, bleh
			Texture2D validTex;
			if(inputs[0] != null)
				validTex = inputs[0];
			else if(inputs[1] != null)
				validTex = inputs[1];
			else if(inputs[2] != null)
				validTex = inputs[2];
			else
				validTex = inputs[3];

			string texPath = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(validTex));

			string path = EditorUtility.SaveFilePanelInProject("Save Texture To Directory", "PackedTexture", "png", "Saved", texPath);
			byte[] pngData = finalTexture.EncodeToPNG();

			//Export to directory
			if(path.Length != 0 && pngData != null) {
				File.WriteAllBytes(path, pngData);
				Debug.Log($"Packed texture saved to: {path}");
				AssetDatabase.Refresh();

				//Disable sRGB
				TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
				importer.sRGBTexture = false;
				importer.SaveAndReimport();
			} else
				EditorUtility.ClearProgressBar();
		}

		private void LoadSettings() {
			try { //Get settings
				settings = (ChannelPackerSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ChannelPackerSettings")[0]), typeof(ChannelPackerSettings)); //Get settings
			} catch { //If no settings file, create and assign
				ChannelPackerSettings created = ScriptableObject.CreateInstance<ChannelPackerSettings>();
				string path = "Assets/Plugins/ChannelPacker/ChannelPackerSettings.asset";
				AssetDatabase.CreateAsset(created, path);
				settings = AssetDatabase.LoadAssetAtPath<ChannelPackerSettings>(path); //CreateAsset doesn't return anything :(
			}

			if(preset == null) {
				if(settings.lastPreset == null) {
					try { //Get preset
						preset = (ChannelPackerPreset)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ChannelPackerPreset")[0]), typeof(ChannelPackerPreset)); //Get settings
					} catch { //If no settings file, create and assign
						ChannelPackerPreset created = ScriptableObject.CreateInstance<ChannelPackerPreset>();
						string path = "Assets/Plugins/ChannelPacker/ChannelPackerDefault.asset";
						AssetDatabase.CreateAsset(created, path);
						preset = AssetDatabase.LoadAssetAtPath<ChannelPackerPreset>(path); //CreateAsset doesn't return anything :(
					}
				} else {
					preset = settings.lastPreset;
				}
			}

			settings.lastPreset = preset;

			EditorUtility.SetDirty(settings);

			//Pull settings from preset
			Array.Copy(preset.defaults, defaults, 4);
			Array.Copy(preset.froms, froms, 4);
			Array.Copy(preset.inverts, inverts, 4);

			//Load preview shader
			previewMat = null;
			Shader preview = Shader.Find(preset.previewShader);
			if(preview != null)
				previewMat = new Material(preview);
			previewShaderFound = previewMat != null;
		}

		private void SavePreset() {
			//Create new preset SO
			string presetPath = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(preset));
			string path = EditorUtility.SaveFilePanelInProject("Save Preset To Directory", "Preset", "asset", "Saved", presetPath);

			if(path.Length != 0) {
				ChannelPackerPreset created = ScriptableObject.Instantiate<ChannelPackerPreset>(preset); //Copy current preset

				//Copy editable values from window
				Array.Copy(defaults, created.defaults, 4);
				Array.Copy(froms, created.froms, 4);
				Array.Copy(inverts, created.inverts, 4);

				AssetDatabase.CreateAsset(created, path);
				preset = AssetDatabase.LoadAssetAtPath<ChannelPackerPreset>(path);

				EditorUtility.SetDirty(preset);

				Debug.Log($"Preset saved to: {path}");
				AssetDatabase.Refresh();
			}
		}

		private void InitGUIStyles() {
			regularStyle = new GUIStyle();
			regularStyle.fontSize = 14;
			regularStyle.fontStyle = FontStyle.Normal;
			regularStyle.wordWrap = true;
			regularStyle.alignment = TextAnchor.MiddleCenter;
			if(EditorGUIUtility.isProSkin)
				regularStyle.normal.textColor = new Color(0.76f, 0.76f, 0.76f, 1f);
			else
				regularStyle.normal.textColor = Color.black;

			regularSmall = new GUIStyle();
			regularSmall.fontSize = 12;
			regularSmall.fontStyle = FontStyle.Normal;
			regularSmall.wordWrap = true;
			regularSmall.alignment = TextAnchor.MiddleCenter;
			if(EditorGUIUtility.isProSkin)
				regularSmall.normal.textColor = new Color(0.76f, 0.76f, 0.76f, 1f);
			else
				regularSmall.normal.textColor = Color.black;

			smallWarn = new GUIStyle();
			smallWarn.fontSize = 12;
			smallWarn.fontStyle = FontStyle.Normal;
			smallWarn.wordWrap = true;
			smallWarn.alignment = TextAnchor.MiddleCenter;
			if(EditorGUIUtility.isProSkin)
				smallWarn.normal.textColor = new Color(0.90f, 0.65f, 0.10f, 1f);
			else
				smallWarn.normal.textColor = new Color(0.60f, 0.35f, 0.00f, 1f);

			regularWarn = new GUIStyle();
			regularWarn.fontSize = 14;
			regularWarn.fontStyle = FontStyle.Normal;
			regularWarn.wordWrap = true;
			regularWarn.alignment = TextAnchor.MiddleCenter;
			if(EditorGUIUtility.isProSkin)
				regularWarn.normal.textColor = new Color(0.90f, 0.65f, 0.10f, 1f);
			else
				regularWarn.normal.textColor = new Color(0.60f, 0.35f, 0.00f, 1f);
		}

		private void GeneratePreview() {
			if(previewAlbedo)
				previewMat.SetTexture("_BaseMap", previewAlbedo);

			if(previewNormal) {
				previewMat.EnableKeyword("_NORMALMAP");
				previewMat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
				previewMat.SetTexture("_NormalMap", previewNormal);
			}
			previewMat.SetFloat("_Metallic", 1f);
			previewMat.EnableKeyword(preset.previewMapKeyword.ToUpper());
			previewMat.SetTexture(preset.previewMapKeyword, finalTexture);

			previewMatViewer = Editor.CreateEditor(previewMat);
		}

		public enum ColorChannel {
			R, G, B, A
		}
	}
}
#endif