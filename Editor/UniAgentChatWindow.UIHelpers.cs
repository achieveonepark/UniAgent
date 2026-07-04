using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Achieve.UniAgent;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Achieve.UniAgent.Editor
{
    /// <summary>
    /// 폰트/입력 필드/버튼 등 공용 UI Toolkit 스타일링 헬퍼를 담당합니다.
    /// </summary>
    public partial class UniAgentChatWindow
    {
        private static Font GetPreferredUiFont()
        {
            if (_preferredUiFont != null)
            {
                return _preferredUiFont;
            }

            try
            {
#pragma warning disable CS0618
                _preferredUiFont = Font.CreateDynamicFontFromOSFont(PreferredUiFontCandidates, 14);
#pragma warning restore CS0618
            }
            catch
            {
                _preferredUiFont = null;
            }

            return _preferredUiFont;
        }

        private static void ApplyPreferredFont(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            var font = GetPreferredUiFont();
            if (font == null)
            {
                return;
            }

#pragma warning disable CS0618
            element.style.unityFont = font;
#pragma warning restore CS0618
        }

        private static void ApplyPreferredFont(TextElement label)
        {
            if (label == null)
            {
                return;
            }

            ApplyPreferredFont((VisualElement)label);
        }

        private static VisualElement QueryBaseFieldInputElement(VisualElement root)
        {
            if (root == null)
            {
                return null;
            }

            return root.Q<VisualElement>(className: BaseFieldInputClassName);
        }

        private static VisualElement QueryTextInputElement(VisualElement root)
        {
            if (root == null)
            {
                return null;
            }

            return root.Q<VisualElement>(className: TextInputClassName)
                ?? root.Q<VisualElement>(className: BaseTextFieldInputClassName)
                ?? QueryBaseFieldInputElement(root);
        }

        private static void SetTextFieldSelectionToEnd(TextField field, int index)
        {
            if (field == null)
            {
                return;
            }

            if (TrySetTextSelectionUsingReflection(field, index))
            {
                return;
            }

#pragma warning disable CS0618
            field.cursorIndex = index;
            field.selectIndex = index;
#pragma warning restore CS0618
        }

        private static bool TrySetTextSelectionUsingReflection(TextField field, int index)
        {
            try
            {
                var textSelectionProp = field.GetType().GetProperty("textSelection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var textSelection = textSelectionProp?.GetValue(field, null);
                if (textSelection == null)
                {
                    return false;
                }

                var selectionType = textSelection.GetType();
                var cursorProp = selectionType.GetProperty("cursorIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var selectProp = selectionType.GetProperty("selectIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var wroteValue = false;
                if (cursorProp != null && cursorProp.CanWrite)
                {
                    cursorProp.SetValue(textSelection, index, null);
                    wroteValue = true;
                }

                if (selectProp != null && selectProp.CanWrite)
                {
                    selectProp.SetValue(textSelection, index, null);
                    wroteValue = true;
                }

                if (wroteValue)
                {
                    return true;
                }

                var selectRangeMethod = selectionType.GetMethod(
                    "SelectRange",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(int) },
                    null);
                if (selectRangeMethod == null)
                {
                    return false;
                }

                selectRangeMethod.Invoke(textSelection, new object[] { index, index });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyPanelSurfaceStyle(VisualElement element, Color background, Color border, float radius)
        {
            if (element == null)
            {
                return;
            }

            element.style.backgroundColor = background;
            element.style.borderTopWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftWidth = 1f;
            element.style.borderRightWidth = 1f;
            element.style.borderTopColor = border;
            element.style.borderBottomColor = border;
            element.style.borderLeftColor = border;
            element.style.borderRightColor = border;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void ApplyButtonStyle(Button button, Color background, Color border, Color textColor, float height, float radius, bool bold = false)
        {
            if (button == null)
            {
                return;
            }

            ApplyPreferredFont(button);
            button.style.height = height;
            button.style.minHeight = height;
            button.style.backgroundColor = background;
            button.style.color = textColor;
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopColor = border;
            button.style.borderBottomColor = border;
            button.style.borderLeftColor = border;
            button.style.borderRightColor = border;
            button.style.borderTopLeftRadius = radius;
            button.style.borderTopRightRadius = radius;
            button.style.borderBottomLeftRadius = radius;
            button.style.borderBottomRightRadius = radius;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        }

        private static void ApplyPopupFieldStyle(PopupField<string> popup, float height)
        {
            if (popup == null)
            {
                return;
            }

            popup.style.height = height;
            popup.style.minHeight = height;
            popup.style.maxHeight = height;
            popup.style.color = UiTextPrimary;

            popup.schedule.Execute(() =>
            {
                var input = QueryBaseFieldInputElement(popup);
                if (input != null)
                {
                    input.style.height = height;
                    input.style.minHeight = height;
                    input.style.maxHeight = height;
                    input.style.paddingLeft = 8f;
                    input.style.paddingRight = 8f;
                    input.style.backgroundColor = UiControlBackground;
                    input.style.borderTopWidth = 1f;
                    input.style.borderBottomWidth = 1f;
                    input.style.borderLeftWidth = 1f;
                    input.style.borderRightWidth = 1f;
                    input.style.borderTopColor = UiControlBorder;
                    input.style.borderBottomColor = UiControlBorder;
                    input.style.borderLeftColor = UiControlBorder;
                    input.style.borderRightColor = UiControlBorder;
                    input.style.borderTopLeftRadius = 7f;
                    input.style.borderTopRightRadius = 7f;
                    input.style.borderBottomLeftRadius = 7f;
                    input.style.borderBottomRightRadius = 7f;
                }

                var text = popup.Q<Label>();
                if (text != null)
                {
                    ApplyPreferredFont(text);
                    text.style.color = UiTextPrimary;
                    text.style.unityTextAlign = TextAnchor.MiddleLeft;
                }

                var arrow = popup.Q<VisualElement>(className: "unity-base-popup-field__arrow")
                    ?? popup.Q<VisualElement>(className: "unity-popup-field__arrow");
                if (arrow != null)
                {
                    arrow.style.unityBackgroundImageTintColor = UiTextSecondary;
                }
            }).ExecuteLater(0);
        }

        private static void StyleTextFieldInput(TextField field, float radius)
        {
            if (field == null)
            {
                return;
            }

            field.style.borderTopWidth = 1f;
            field.style.borderBottomWidth = 1f;
            field.style.borderLeftWidth = 1f;
            field.style.borderRightWidth = 1f;
            field.style.borderTopColor = UiControlBorder;
            field.style.borderBottomColor = UiControlBorder;
            field.style.borderLeftColor = UiControlBorder;
            field.style.borderRightColor = UiControlBorder;
            field.style.borderTopLeftRadius = radius;
            field.style.borderTopRightRadius = radius;
            field.style.borderBottomLeftRadius = radius;
            field.style.borderBottomRightRadius = radius;

            field.schedule.Execute(() =>
            {
                var input = QueryTextInputElement(field);
                if (input != null)
                {
                    input.style.backgroundColor = UiControlBackground;
                    input.style.color = UiTextPrimary;
                    input.style.paddingLeft = 8f;
                    input.style.paddingRight = 8f;
                    input.style.borderTopLeftRadius = radius;
                    input.style.borderTopRightRadius = radius;
                    input.style.borderBottomLeftRadius = radius;
                    input.style.borderBottomRightRadius = radius;
                }

                var text = field.Q<TextElement>();
                if (text != null)
                {
                    ApplyPreferredFont(text);
                    text.style.color = UiTextPrimary;
                }
            }).ExecuteLater(0);
        }

        /// <summary>
        /// 세션 토큰 사용량을 표시하는 경량 원형 게이지입니다.
        /// </summary>
        private sealed class UniAgentTokenGaugeElement : VisualElement
        {
            private float _progress;
            private Color _fillColor = new Color(0.17f, 0.56f, 0.94f, 1f);
            private readonly Color _trackColor = new Color(0.26f, 0.30f, 0.36f, 1f);
            private readonly Color _centerColor = new Color(0.10f, 0.12f, 0.15f, 1f);

            /// <summary>[0, 1] 범위의 진행률 값입니다.</summary>
            public float Progress
            {
                get => _progress;
                set
                {
                    var clamped = Mathf.Clamp01(value);
                    if (Mathf.Approximately(_progress, clamped))
                    {
                        return;
                    }

                    _progress = clamped;
                    MarkDirtyRepaint();
                }
            }

            /// <summary>채워진 호(arc)에 사용할 색상입니다.</summary>
            public Color FillColor
            {
                get => _fillColor;
                set
                {
                    if (_fillColor.Equals(value))
                    {
                        return;
                    }

                    _fillColor = value;
                    MarkDirtyRepaint();
                }
            }

            /// <summary>토큰 게이지 UI 요소를 생성합니다.</summary>
            public UniAgentTokenGaugeElement()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                var rect = contentRect;
                if (rect.width <= 1f || rect.height <= 1f)
                {
                    return;
                }

                var center = rect.center;
                var radius = Mathf.Max(1f, Mathf.Min(rect.width, rect.height) * 0.5f - 1f);
                const float lineWidth = 4f;
                var innerRadius = Mathf.Max(1f, radius - lineWidth - 1f);

                var painter = context.painter2D;
                painter.lineWidth = lineWidth;

                painter.fillColor = _centerColor;
                painter.BeginPath();
                painter.Arc(center, innerRadius, 0f, Mathf.PI * 2f);
                painter.Fill();

                painter.strokeColor = _trackColor;
                painter.BeginPath();
                painter.Arc(center, radius, 0f, Mathf.PI * 2f);
                painter.Stroke();

                if (_progress <= 0.0001f)
                {
                    return;
                }

                painter.strokeColor = _fillColor;
                painter.BeginPath();
                var start = -Mathf.PI * 0.5f;
                var end = start + Mathf.PI * 2f * _progress;
                painter.Arc(center, radius, start, end);
                painter.Stroke();
            }
        }

    }
}
