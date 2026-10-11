using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.ui;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VCam.AvatarExporter
{
    internal sealed class AvatarExporterWindow : EditorWindow
    {
        private VRCAvatarDescriptor _avatar;
        private readonly ExportOptions _options = new();
        private string _authors = "";
        private bool _termsChecked;
        private IReadOnlyList<Message> _problems = Array.Empty<Message>();
        private ExportReport _report;
        private Vector2 _scroll;

        private static readonly int[] TextureSizes = { 1024, 2048, 4096, 0 };

        /// <summary>The labels beside fields. The label column widens to the longest in the editor language.</summary>
        private static readonly string[] FieldLabels =
        {
            "Window:Avatar", "Window:Name", "Window:Authors", "Window:Version", "Window:Copyright", "Window:Contact",
            "Window:Thumbnail", "Window:MaxTextureSize", "Window:Winks", "Window:AvatarPermission", "Window:CommercialUsage",
            "Window:CreditNotation", "Window:Modification", "Window:Redistribution", "Window:ViolentUsage", "Window:SexualUsage",
            "Window:PoliticalOrReligiousUsage", "Window:AntisocialOrHateUsage",
        };

        private const float MinLabelWidth = 150;
        private const float LabelPadding = 8;

        [MenuItem("Tools/VCam Avatar Exporter")]
        private static void Open() => GetWindow<AvatarExporterWindow>("VCam Avatar Exporter");

        private void OnEnable() => _problems = DependencyVersions.FindProblems();

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = Mathf.Max(
                MinLabelWidth, FieldLabels.Max(key => EditorStyles.label.CalcSize(new GUIContent(Strings.Tr(key))).x) + LabelPadding);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            LanguageSwitcher.DrawImmediate();
            EditorGUILayout.Space();
            if (_problems.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", _problems), MessageType.Error);
            }

            EditorGUI.BeginChangeCheck();
            _avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(Strings.Tr("Window:Avatar"), _avatar, typeof(VRCAvatarDescriptor), true);
            if (EditorGUI.EndChangeCheck() && _avatar != null && string.IsNullOrEmpty(_options.Meta.Name))
            {
                _options.Meta.Name = _avatar.gameObject.name;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Strings.Tr("Window:Information"), EditorStyles.boldLabel);
            _options.Meta.Name = EditorGUILayout.TextField(Strings.Tr("Window:Name"), _options.Meta.Name);
            _authors = EditorGUILayout.TextField(Content("Window:Authors"), _authors);
            _options.Meta.Version = EditorGUILayout.TextField(Strings.Tr("Window:Version"), _options.Meta.Version);
            _options.Meta.CopyrightInformation = EditorGUILayout.TextField(Strings.Tr("Window:Copyright"), _options.Meta.CopyrightInformation);
            _options.Meta.ContactInformation = EditorGUILayout.TextField(Strings.Tr("Window:Contact"), _options.Meta.ContactInformation);
            _options.Meta.Thumbnail = (Texture2D)EditorGUILayout.ObjectField(Strings.Tr("Window:Thumbnail"), _options.Meta.Thumbnail, typeof(Texture2D), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Strings.Tr("Window:Output"), EditorStyles.boldLabel);
            _options.MaxTextureSize = EditorGUILayout.IntPopup(
                Content("Window:MaxTextureSize"), _options.MaxTextureSize, TextureSizeLabels(), TextureSizes);
            _options.SplitsBlink = EditorGUILayout.Toggle(Content("Window:Winks"), _options.SplitsBlink);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Strings.Tr("Window:License"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(Strings.Tr("Window:LicenseHint"), MessageType.None);
            _options.Meta.AvatarPermission = Popup("Window:AvatarPermission", _options.Meta.AvatarPermission);
            _options.Meta.CommercialUsage = Popup("Window:CommercialUsage", _options.Meta.CommercialUsage);
            _options.Meta.CreditNotation = Popup("Window:CreditNotation", _options.Meta.CreditNotation);
            _options.Meta.Modification = Popup("Window:Modification", _options.Meta.Modification);
            _options.Meta.Redistribution = EditorGUILayout.Toggle(Strings.Tr("Window:Redistribution"), _options.Meta.Redistribution);
            _options.Meta.ViolentUsage = EditorGUILayout.Toggle(Strings.Tr("Window:ViolentUsage"), _options.Meta.ViolentUsage);
            _options.Meta.SexualUsage = EditorGUILayout.Toggle(Strings.Tr("Window:SexualUsage"), _options.Meta.SexualUsage);
            _options.Meta.PoliticalOrReligiousUsage = EditorGUILayout.Toggle(Strings.Tr("Window:PoliticalOrReligiousUsage"), _options.Meta.PoliticalOrReligiousUsage);
            _options.Meta.AntisocialOrHateUsage = EditorGUILayout.Toggle(Strings.Tr("Window:AntisocialOrHateUsage"), _options.Meta.AntisocialOrHateUsage);

            EditorGUILayout.Space();
            // ToggleLeft keeps its label on one line, which cuts the sentence off in a narrow window.
            using (new EditorGUILayout.HorizontalScope())
            {
                _termsChecked = EditorGUILayout.Toggle(_termsChecked, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button(Strings.Tr("Window:TermsChecked"), EditorStyles.wordWrappedLabel))
                {
                    _termsChecked = !_termsChecked;
                }
            }

            _options.Meta.Authors = _authors.Split(',').Select(author => author.Trim()).Where(author => author.Length > 0).ToList();
            using (new EditorGUI.DisabledScope(!CanExport))
            {
                if (GUILayout.Button(Strings.Tr("Window:Export")))
                {
                    Export();
                }
            }

            if (_report != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(Strings.Tr("Window:LastExport"), EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_report.Notes.Count == 0 ? Strings.Tr("Window:NoNotes") : string.Join("\n", _report.Notes), MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        private static GUIContent Content(string key) => new(Strings.Tr(key), Strings.Tr(key + "Tooltip"));

        private static GUIContent[] TextureSizeLabels() =>
            TextureSizes.Select(size => new GUIContent(size == 0 ? Strings.Tr("Window:OriginalSize") : size.ToString())).ToArray();

        /// <summary>A popup of the values of a VRM license enum, with the names the localization gives each value.</summary>
        private static T Popup<T>(string key, T value) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var labels = values.Select(option => Strings.Tr(EnumKey(option))).ToArray();
            return values[EditorGUILayout.Popup(Strings.Tr(key), Array.IndexOf(values, value), labels)];
        }

        private static string EnumKey<T>(T value) where T : Enum => $"{typeof(T).Name}:{value}";

        private bool CanExport =>
            _problems.Count == 0 && _avatar != null && _termsChecked
            && !string.IsNullOrWhiteSpace(_options.Meta.Name) && _options.Meta.Authors.Count > 0;

        private void Export()
        {
            var path = EditorUtility.SaveFilePanel(Strings.Tr("Window:Export"), "", _options.Meta.Name + ".vrm", "vrm");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                EditorUtility.DisplayProgressBar("VCam Avatar Exporter", Strings.Tr("Window:Exporting"), 0.5f);
                _report = AvatarExporter.Export(_avatar.gameObject, _options, path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("VCam Avatar Exporter", e.Message, Strings.Tr("Window:OK"));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
