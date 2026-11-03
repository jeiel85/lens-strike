#nullable enable
// 코드로 만드는 uGUI. 씬·프리팹 파일 없이 화면을 구성해 변경 이력을 코드로만 관리한다.
// 한글은 기기 글꼴(안드로이드 Noto Sans CJK 등)을 동적 글꼴로 불러 쓴다.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LensStrike.App
{
    public static class Ui
    {
        private static Font? font;

        public static readonly Color Panel = new Color(0.063f, 0.075f, 0.106f, 0.94f);
        public static readonly Color Accent = Hex("#ffd166");
        public static readonly Color Danger = Hex("#ff4d4d");
        public static readonly Color Ok = Hex("#4ade80");
        public static readonly Color Muted = Hex("#9aa3b2");
        public static readonly Color Dim = new Color(0, 0, 0, 0.5f);

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Noto Sans CJK KR", "NotoSansCJK-Regular", "Noto Sans KR", "SamsungOneKorean", "Apple SD Gothic Neo",
                    "Malgun Gothic", "Roboto", "Arial",
                }, 32);
                return font;
            }
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform r, float l = 0, float t = 0, float rr = 0, float b = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(l, b);
            r.offsetMax = new Vector2(-rr, -t);
            return r;
        }

        /// <summary>앵커(0~1)와 피벗, 위치, 크기를 한 번에 지정</summary>
        public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot ?? anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static Image Image(string name, Transform parent, Color color, bool raycast = false)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var r = Rect(name, parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Color bg, Color fg, Action onClick, int fontSize = 40)
        {
            var img = Image(name, parent, bg, raycast: true);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1, 1, 1, 0.95f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1);
            colors.disabledColor = new Color(1, 1, 1, 0.35f);
            btn.colors = colors;
            var t = Text("Label", img.transform, label, fontSize, fg, style: FontStyle.Bold);
            Stretch(t.rectTransform, 8, 4, 8, 4);
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void SetLabel(Button b, string label) => b.GetComponentInChildren<Text>().text = label;

        public static InputField Input(string name, Transform parent, string placeholder, int charLimit, int fontSize = 40)
        {
            var bg = Image(name, parent, new Color(1, 1, 1, 0.08f), raycast: true);
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Text("Text", bg.transform, "", fontSize, Color.white, TextAnchor.MiddleCenter);
            text.supportRichText = false;
            Stretch(text.rectTransform, 20, 4, 20, 4);
            var ph = Text("Placeholder", bg.transform, placeholder, fontSize, Muted, TextAnchor.MiddleCenter);
            Stretch(ph.rectTransform, 20, 4, 20, 4);
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = charLimit;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        /// <summary>눌렀다 뗐을 때를 알려 주는 투명 영역(사격 영역)</summary>
        public sealed class PressArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public Action? Down;
            public Action? Up;
            private int pressed;

            public void OnPointerDown(PointerEventData e)
            {
                pressed++;
                if (pressed == 1) Down?.Invoke();
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (pressed == 0) return;
                pressed--;
                if (pressed == 0) Up?.Invoke();
            }

            private void OnDisable()
            {
                if (pressed > 0) Up?.Invoke();
                pressed = 0;
            }
        }
    }
}
