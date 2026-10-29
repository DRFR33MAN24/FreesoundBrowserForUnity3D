using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    /// <summary>
    /// Draws every AudioClip field as the normal object field plus a small Freesound search button.
    /// </summary>
    [CustomPropertyDrawer(typeof(AudioClip))]
    public class AudioClipFreesoundDrawer : PropertyDrawer
    {
        const float ButtonWidth = 24f;
        const float Spacing = 2f;

        static GUIContent buttonContent;

        static GUIContent ButtonContent
        {
            get
            {
                if (buttonContent == null)
                {
                    var icon = EditorGUIUtility.IconContent("Search Icon").image;
                    buttonContent = icon != null
                        ? new GUIContent(icon, "Find a sound on Freesound for this field")
                        : new GUIContent("FS", "Find a sound on Freesound for this field");
                }
                return buttonContent;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.LabelField(position, label.text, "Use AudioClip fields only.");
                return;
            }

            if (!FreesoundSettings.Instance.showFieldButton)
            {
                EditorGUI.ObjectField(position, property, typeof(AudioClip), label);
                return;
            }

            var fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - Spacing, position.height);
            var buttonRect = new Rect(fieldRect.xMax + Spacing, position.y, ButtonWidth, EditorGUIUtility.singleLineHeight);

            EditorGUI.ObjectField(fieldRect, property, typeof(AudioClip), label);

            if (GUI.Button(buttonRect, ButtonContent, EditorStyles.miniButton))
            {
                FreesoundBrowserWindow.OpenForProperty(property);
                GUIUtility.ExitGUI();
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
