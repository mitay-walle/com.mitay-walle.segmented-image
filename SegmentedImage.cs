using System;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AnimatedValues;
#endif
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.Serialization;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI (Canvas)/Segmented Image", 12)]
public class SegmentedImage : Image
{
    public enum ValueMode
    {
        Int,
        Float
    }

    [SerializeField, Min(1)] private int m_SegmentCount = 5;
    [SerializeField, Min(0f)] private float m_Spacing = 2f;
    [SerializeField] private Slider.Direction m_Direction = Slider.Direction.LeftToRight;
    [SerializeField] private int[] m_DrawMaskWords = { -1 };
    [SerializeField] private ValueMode m_ValueMode = ValueMode.Float;
    [SerializeField, HideInInspector, FormerlySerializedAs("m_Axis")] private RectTransform.Axis m_LegacyAxis = RectTransform.Axis.Horizontal;
    [SerializeField, HideInInspector, FormerlySerializedAs("m_FillInvert")] private bool m_LegacyFillInvert;
    [SerializeField, HideInInspector] private int m_SerializationVersion;
    [SerializeField, Min(1)] private int m_IntMaxValue = 5;
    [SerializeField, Min(0.0001f)] private float m_FloatMaxValue = 1f;

    private static readonly Vector2[] s_VertScratch = new Vector2[4];
    private static readonly Vector2[] s_UVScratch = new Vector2[4];
    private static readonly Vector3[] s_Xy = new Vector3[4];
    private static readonly Vector3[] s_Uv = new Vector3[4];

    public int segmentCount
    {
        get => m_SegmentCount;
        set
        {
            value = Mathf.Max(1, value);
            if (m_SegmentCount == value)
                return;

            m_SegmentCount = value;
            EnsureDrawMaskSize(true);
            SetAllDirty();
        }
    }

    public float spacing
    {
        get => m_Spacing;
        set
        {
            value = Mathf.Max(0f, value);
            if (Mathf.Approximately(m_Spacing, value))
                return;

            m_Spacing = value;
            SetAllDirty();
        }
    }

    public Slider.Direction direction
    {
        get => m_Direction;
        set
        {
            if (m_Direction == value)
                return;

            m_Direction = value;
            SetAllDirty();
        }
    }

    public RectTransform.Axis axis
    {
        get => IsHorizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
        set => direction = value == RectTransform.Axis.Horizontal
            ? Slider.Direction.LeftToRight
            : Slider.Direction.BottomToTop;
    }

    public ValueMode valueMode
    {
        get => m_ValueMode;
        set
        {
            if (m_ValueMode == value)
                return;

            m_ValueMode = value;
            SetAllDirty();
        }
    }

    public int intMaxValue
    {
        get => m_IntMaxValue;
        set
        {
            int newMaxValue = Mathf.Max(1, value);
            if (m_IntMaxValue == newMaxValue)
                return;

            int currentValue = intValue;
            m_IntMaxValue = newMaxValue;
            fillAmount = Mathf.Clamp(currentValue, 0, m_IntMaxValue) / (float)m_IntMaxValue;
            SetAllDirty();
        }
    }

    public int intValue
    {
        get => Mathf.Clamp(Mathf.RoundToInt(fillAmount * m_IntMaxValue), 0, m_IntMaxValue);
        set
        {
            int maxValue = Mathf.Max(1, m_IntMaxValue);
            float normalizedValue = Mathf.Clamp(value, 0, maxValue) / (float)maxValue;
            if (Mathf.Approximately(fillAmount, normalizedValue))
                return;

            fillAmount = normalizedValue;
            SetAllDirty();
        }
    }

    public float floatMaxValue
    {
        get => m_FloatMaxValue;
        set
        {
            float newMaxValue = Mathf.Max(0.0001f, value);
            if (Mathf.Approximately(m_FloatMaxValue, newMaxValue))
                return;

            float currentValue = floatValue;
            m_FloatMaxValue = newMaxValue;
            fillAmount = Mathf.Clamp(currentValue, 0f, m_FloatMaxValue) / m_FloatMaxValue;
            SetAllDirty();
        }
    }

    public float floatValue
    {
        get => fillAmount * m_FloatMaxValue;
        set
        {
            float maxValue = Mathf.Max(0.0001f, m_FloatMaxValue);
            float normalizedValue = Mathf.Clamp(value, 0f, maxValue) / maxValue;
            if (Mathf.Approximately(fillAmount, normalizedValue))
                return;

            fillAmount = normalizedValue;
            SetAllDirty();
        }
    }

    public BitArray drawMask
    {
        get
        {
            EnsureDrawMaskSize(false);
            var result = new BitArray(m_DrawMaskWords) { Length = m_SegmentCount };
            return result;
        }
        set
        {
            EnsureDrawMaskSize(false);

            var mask = value == null ? new BitArray(m_SegmentCount, true) : new BitArray(value);
            mask.Length = m_SegmentCount;
            var words = new int[WordCountFor(m_SegmentCount)];
            mask.CopyTo(words, 0);
            m_DrawMaskWords = words;
            SetAllDirty();
        }
    }

    public bool GetSegmentVisible(int index)
    {
        if ((uint)index >= (uint)m_SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        EnsureDrawMaskSize(false);
        int word = index >> 5;
        int bit = index & 31;
        return (m_DrawMaskWords[word] & (1 << bit)) != 0;
    }


    private bool GetSegmentVisibleUnchecked(int index)
    {
        int word = index >> 5;
        int bit = index & 31;
        return (m_DrawMaskWords[word] & (1 << bit)) != 0;
    }

    public void SetSegmentVisible(int index, bool visible)
    {
        if ((uint)index >= (uint)m_SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        EnsureDrawMaskSize(false);
        int word = index >> 5;
        int bit = index & 31;
        int mask = 1 << bit;

        if (visible)
            m_DrawMaskWords[word] |= mask;
        else
            m_DrawMaskWords[word] &= ~mask;

        SetAllDirty();
    }

    public void SetValue(int value, int maxValue)
    {
        valueMode = ValueMode.Int;
        intMaxValue = maxValue;
        intValue = value;
    }

    public void SetValue(float value, float maxValue)
    {
        valueMode = ValueMode.Float;
        floatMaxValue = maxValue;
        floatValue = value;
    }

    public override void SetNativeSize()
    {
        Sprite activeSprite = overrideSprite;
        if (activeSprite == null)
            return;

        float width = activeSprite.rect.width / pixelsPerUnit;
        float height = activeSprite.rect.height / pixelsPerUnit;
        float gaps = m_Spacing * Mathf.Max(0, m_SegmentCount - 1);

        if (IsHorizontal)
            width = width * m_SegmentCount + gaps;
        else
            height = height * m_SegmentCount + gaps;

        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = new Vector2(width, height);
        SetAllDirty();
    }

    public override float preferredWidth
    {
        get
        {
            Sprite activeSprite = overrideSprite;
            if (activeSprite == null)
                return 0f;

            float width = type == Type.Sliced || type == Type.Tiled
                ? DataUtility.GetMinSize(activeSprite).x / pixelsPerUnit
                : activeSprite.rect.size.x / pixelsPerUnit;

            if (!IsHorizontal)
                return width;

            return width * m_SegmentCount + m_Spacing * Mathf.Max(0, m_SegmentCount - 1);
        }
    }

    public override float preferredHeight
    {
        get
        {
            Sprite activeSprite = overrideSprite;
            if (activeSprite == null)
                return 0f;

            float height = type == Type.Sliced || type == Type.Tiled
                ? DataUtility.GetMinSize(activeSprite).y / pixelsPerUnit
                : activeSprite.rect.size.y / pixelsPerUnit;

            if (IsHorizontal)
                return height;

            return height * m_SegmentCount + m_Spacing * Mathf.Max(0, m_SegmentCount - 1);
        }
    }

    protected override void OnEnable()
    {
        EnsureDrawMaskSize(true);
        base.OnEnable();
    }

    public override void OnBeforeSerialize()
    {
        base.OnBeforeSerialize();
        m_SerializationVersion = 1;
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();

        if (m_SerializationVersion == 0)
        {
            m_Direction = m_LegacyAxis == RectTransform.Axis.Horizontal
                ? (m_LegacyFillInvert ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight)
                : (m_LegacyFillInvert ? Slider.Direction.TopToBottom : Slider.Direction.BottomToTop);
            m_SerializationVersion = 1;
        }

        m_SegmentCount = Mathf.Max(1, m_SegmentCount);
        m_Spacing = Mathf.Max(0f, m_Spacing);
        m_IntMaxValue = Mathf.Max(1, m_IntMaxValue);
        m_FloatMaxValue = Mathf.Max(0.0001f, m_FloatMaxValue);
        EnsureDrawMaskSize(true);
    }

    protected override void OnPopulateMesh(VertexHelper toFill)
    {
        toFill.Clear();

        int count = Mathf.Max(1, m_SegmentCount);
        Rect fullRect = GetPixelAdjustedRect();
        float totalSpacing = m_Spacing * Mathf.Max(0, count - 1);

        float segmentSize = IsHorizontal
            ? (fullRect.width - totalSpacing) / count
            : (fullRect.height - totalSpacing) / count;

        if (segmentSize <= 0f)
            return;

        EnsureDrawMaskSize(false);
        Sprite activeSprite = overrideSprite;
        bool reverseSequence = IsFillSequenceReversed;
        float totalFill = Mathf.Clamp01(fillAmount) * count;

        for (int slot = 0; slot < count; slot++)
        {
            if (!GetSegmentVisibleUnchecked(slot))
                continue;

            Rect segmentRect = GetSegmentRect(fullRect, slot, segmentSize);
            int fillIndex = reverseSequence ? count - 1 - slot : slot;
            float segmentFill = Mathf.Clamp01(totalFill - fillIndex);

            if (type != Type.Filled && segmentFill < 0.999999f)
                continue;

            if (activeSprite == null)
            {
                AddQuad(toFill, segmentRect.min, segmentRect.max, color, Vector2.zero, Vector2.zero);
                continue;
            }

            switch (type)
            {
                case Type.Simple:
                    if (useSpriteMesh)
                        GenerateSprite(toFill, segmentRect, preserveAspect);
                    else
                        GenerateSimpleSprite(toFill, segmentRect, preserveAspect);
                    break;
                case Type.Sliced:
                    GenerateSlicedSprite(toFill, segmentRect);
                    break;
                case Type.Tiled:
                    GenerateTiledSprite(toFill, segmentRect, count);
                    break;
                case Type.Filled:
                    GenerateFilledSprite(toFill, segmentRect, preserveAspect, segmentFill);
                    break;
            }
        }
    }

    private bool IsHorizontal =>
        m_Direction == Slider.Direction.LeftToRight ||
        m_Direction == Slider.Direction.RightToLeft;

    private bool IsFillSequenceReversed =>
        m_Direction == Slider.Direction.RightToLeft ||
        m_Direction == Slider.Direction.TopToBottom;

    private Rect GetSegmentRect(Rect fullRect, int index, float segmentSize)
    {
        if (IsHorizontal)
        {
            float x = fullRect.x + index * (segmentSize + m_Spacing);
            return new Rect(x, fullRect.y, segmentSize, fullRect.height);
        }

        float y = fullRect.y + index * (segmentSize + m_Spacing);
        return new Rect(fullRect.x, y, fullRect.width, segmentSize);
    }

    private void GenerateSimpleSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect)
    {
        Vector4 v = GetDrawingDimensions(rect, shouldPreserveAspect);
        Vector4 uv = DataUtility.GetOuterUV(overrideSprite);
        Color32 color32 = color;

        vh.AddVert(new Vector3(v.x, v.y), color32, new Vector2(uv.x, uv.y));
        vh.AddVert(new Vector3(v.x, v.w), color32, new Vector2(uv.x, uv.w));
        vh.AddVert(new Vector3(v.z, v.w), color32, new Vector2(uv.z, uv.w));
        vh.AddVert(new Vector3(v.z, v.y), color32, new Vector2(uv.z, uv.y));

        int start = vh.currentVertCount - 4;
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    private void GenerateSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect)
    {
        Sprite activeSprite = overrideSprite;
        Vector2 spriteSize = activeSprite.rect.size;
        Vector2 spritePivot = activeSprite.pivot / spriteSize;
        Vector2 rectPivot = rectTransform.pivot;
        Vector2 pivotPosition = rect.position + Vector2.Scale(rectPivot, rect.size);

        if (shouldPreserveAspect && spriteSize.sqrMagnitude > 0f)
            PreserveSpriteAspectRatio(ref rect, spriteSize);

        Vector2 drawingSize = rect.size;
        Vector2 spriteBoundSize = activeSprite.bounds.size;
        Vector2 drawOffset = (rectPivot - spritePivot) * drawingSize;
        Color32 color32 = color;
        int start = vh.currentVertCount;

        Vector2[] vertices = activeSprite.vertices;
        Vector2[] uvs = activeSprite.uv;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 position = new Vector2(
                vertices[i].x / spriteBoundSize.x * drawingSize.x - drawOffset.x,
                vertices[i].y / spriteBoundSize.y * drawingSize.y - drawOffset.y) + pivotPosition;

            vh.AddVert(position, color32, uvs[i]);
        }

        ushort[] triangles = activeSprite.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
            vh.AddTriangle(start + triangles[i], start + triangles[i + 1], start + triangles[i + 2]);
    }

    private void GenerateSlicedSprite(VertexHelper vh, Rect rect)
    {
        if (!hasBorder)
        {
            GenerateSimpleSprite(vh, rect, false);
            return;
        }

        Sprite activeSprite = overrideSprite;
        Vector4 outer = DataUtility.GetOuterUV(activeSprite);
        Vector4 inner = DataUtility.GetInnerUV(activeSprite);
        Vector4 padding = DataUtility.GetPadding(activeSprite);
        Vector4 border = activeSprite.border;
        Vector4 adjustedBorders = GetAdjustedBorders(border / multipliedPixelsPerUnit, rect);
        padding /= multipliedPixelsPerUnit;

        s_VertScratch[0] = new Vector2(padding.x, padding.y);
        s_VertScratch[3] = new Vector2(rect.width - padding.z, rect.height - padding.w);
        s_VertScratch[1] = new Vector2(adjustedBorders.x, adjustedBorders.y);
        s_VertScratch[2] = new Vector2(rect.width - adjustedBorders.z, rect.height - adjustedBorders.w);

        for (int i = 0; i < 4; i++)
            s_VertScratch[i] += rect.position;

        s_UVScratch[0] = new Vector2(outer.x, outer.y);
        s_UVScratch[1] = new Vector2(inner.x, inner.y);
        s_UVScratch[2] = new Vector2(inner.z, inner.w);
        s_UVScratch[3] = new Vector2(outer.z, outer.w);

        for (int x = 0; x < 3; x++)
        {
            int x2 = x + 1;
            for (int y = 0; y < 3; y++)
            {
                if (!fillCenter && x == 1 && y == 1)
                    continue;

                int y2 = y + 1;
                if (s_VertScratch[x2].x - s_VertScratch[x].x <= 0f || s_VertScratch[y2].y - s_VertScratch[y].y <= 0f)
                    continue;

                AddQuad(
                    vh,
                    new Vector2(s_VertScratch[x].x, s_VertScratch[y].y),
                    new Vector2(s_VertScratch[x2].x, s_VertScratch[y2].y),
                    color,
                    new Vector2(s_UVScratch[x].x, s_UVScratch[y].y),
                    new Vector2(s_UVScratch[x2].x, s_UVScratch[y2].y));
            }
        }
    }

    private void GenerateTiledSprite(VertexHelper vh, Rect rect, int segmentCountForBudget)
    {
        Sprite activeSprite = overrideSprite;
        Vector4 outer = DataUtility.GetOuterUV(activeSprite);
        Vector4 inner = DataUtility.GetInnerUV(activeSprite);
        Vector4 border = activeSprite.border;
        Vector2 spriteSize = activeSprite.rect.size;
        float tileWidth = (spriteSize.x - border.x - border.z) / multipliedPixelsPerUnit;
        float tileHeight = (spriteSize.y - border.y - border.w) / multipliedPixelsPerUnit;

        border = GetAdjustedBorders(border / multipliedPixelsPerUnit, rect);

        Vector2 uvMin = new Vector2(inner.x, inner.y);
        Vector2 uvMax = new Vector2(inner.z, inner.w);
        float xMin = border.x;
        float xMax = rect.width - border.z;
        float yMin = border.y;
        float yMax = rect.height - border.w;
        Vector2 clipped = uvMax;

        if (tileWidth <= 0f)
            tileWidth = xMax - xMin;
        if (tileHeight <= 0f)
            tileHeight = yMax - yMin;

        if (activeSprite != null && (hasBorder || activeSprite.packed || activeSprite.texture != null && activeSprite.texture.wrapMode != TextureWrapMode.Repeat))
        {
            long nTilesW = 0;
            long nTilesH = 0;
            int maxVertices = Mathf.Max(64, 65000 / Mathf.Max(1, segmentCountForBudget));

            if (fillCenter)
            {
                nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);
                double nVertices = hasBorder
                    ? (nTilesW + 2.0) * (nTilesH + 2.0) * 4.0
                    : nTilesW * nTilesH * 4.0;

                if (nVertices > maxVertices)
                {
                    double maxTiles = maxVertices / 4.0;
                    double imageRatio = hasBorder
                        ? (nTilesW + 2.0) / Math.Max(1.0, nTilesH + 2.0)
                        : (double)nTilesW / Math.Max(1.0, nTilesH);

                    double targetTilesW = Math.Sqrt(maxTiles * imageRatio);
                    double targetTilesH = Math.Sqrt(maxTiles / imageRatio);

                    if (hasBorder)
                    {
                        targetTilesW -= 2.0;
                        targetTilesH -= 2.0;
                    }

                    nTilesW = Math.Max(1, (long)Math.Floor(targetTilesW));
                    nTilesH = Math.Max(1, (long)Math.Floor(targetTilesH));
                    tileWidth = (xMax - xMin) / nTilesW;
                    tileHeight = (yMax - yMin) / nTilesH;
                }
            }
            else if (hasBorder)
            {
                nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);
                double nVertices = (nTilesH + nTilesW + 2.0) * 2.0 * 4.0;

                if (nVertices > maxVertices)
                {
                    double maxTiles = maxVertices / 4.0;
                    double imageRatio = (double)nTilesW / Math.Max(1.0, nTilesH);
                    double targetTilesW = (maxTiles - 4.0) / (2.0 * (1.0 + imageRatio));
                    double targetTilesH = targetTilesW * imageRatio;
                    nTilesW = Math.Max(1, (long)Math.Floor(targetTilesW));
                    nTilesH = Math.Max(1, (long)Math.Floor(targetTilesH));
                    tileWidth = (xMax - xMin) / nTilesW;
                    tileHeight = (yMax - yMin) / nTilesH;
                }
            }

            if (fillCenter)
            {
                for (long j = 0; j < nTilesH; j++)
                {
                    float y1 = yMin + j * tileHeight;
                    float y2 = yMin + (j + 1) * tileHeight;
                    if (y2 > yMax)
                    {
                        clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                        y2 = yMax;
                    }

                    clipped.x = uvMax.x;
                    for (long i = 0; i < nTilesW; i++)
                    {
                        float x1 = xMin + i * tileWidth;
                        float x2 = xMin + (i + 1) * tileWidth;
                        if (x2 > xMax)
                        {
                            clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                            x2 = xMax;
                        }

                        AddQuad(vh, new Vector2(x1, y1) + rect.position, new Vector2(x2, y2) + rect.position, color, uvMin, clipped);
                    }
                }
            }

            if (hasBorder)
            {
                clipped = uvMax;
                for (long j = 0; j < nTilesH; j++)
                {
                    float y1 = yMin + j * tileHeight;
                    float y2 = yMin + (j + 1) * tileHeight;
                    if (y2 > yMax)
                    {
                        clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                        y2 = yMax;
                    }

                    AddQuad(vh, new Vector2(0f, y1) + rect.position, new Vector2(xMin, y2) + rect.position, color, new Vector2(outer.x, uvMin.y), new Vector2(uvMin.x, clipped.y));
                    AddQuad(vh, new Vector2(xMax, y1) + rect.position, new Vector2(rect.width, y2) + rect.position, color, new Vector2(uvMax.x, uvMin.y), new Vector2(outer.z, clipped.y));
                }

                clipped = uvMax;
                for (long i = 0; i < nTilesW; i++)
                {
                    float x1 = xMin + i * tileWidth;
                    float x2 = xMin + (i + 1) * tileWidth;
                    if (x2 > xMax)
                    {
                        clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                        x2 = xMax;
                    }

                    AddQuad(vh, new Vector2(x1, 0f) + rect.position, new Vector2(x2, yMin) + rect.position, color, new Vector2(uvMin.x, outer.y), new Vector2(clipped.x, uvMin.y));
                    AddQuad(vh, new Vector2(x1, yMax) + rect.position, new Vector2(x2, rect.height) + rect.position, color, new Vector2(uvMin.x, uvMax.y), new Vector2(clipped.x, outer.w));
                }

                AddQuad(vh, new Vector2(0f, 0f) + rect.position, new Vector2(xMin, yMin) + rect.position, color, new Vector2(outer.x, outer.y), new Vector2(uvMin.x, uvMin.y));
                AddQuad(vh, new Vector2(xMax, 0f) + rect.position, new Vector2(rect.width, yMin) + rect.position, color, new Vector2(uvMax.x, outer.y), new Vector2(outer.z, uvMin.y));
                AddQuad(vh, new Vector2(0f, yMax) + rect.position, new Vector2(xMin, rect.height) + rect.position, color, new Vector2(outer.x, uvMax.y), new Vector2(uvMin.x, outer.w));
                AddQuad(vh, new Vector2(xMax, yMax) + rect.position, new Vector2(rect.width, rect.height) + rect.position, color, new Vector2(uvMax.x, uvMax.y), new Vector2(outer.z, outer.w));
            }
        }
        else
        {
            Vector2 uvScale = new Vector2((xMax - xMin) / tileWidth, (yMax - yMin) / tileHeight);
            if (fillCenter)
                AddQuad(vh, new Vector2(xMin, yMin) + rect.position, new Vector2(xMax, yMax) + rect.position, color, Vector2.Scale(uvMin, uvScale), Vector2.Scale(uvMax, uvScale));
        }
    }

    private void GenerateFilledSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect, float segmentFillAmount)
    {
        if (segmentFillAmount < 0.001f)
            return;

        Vector4 v = GetDrawingDimensions(rect, shouldPreserveAspect);
        Vector4 outer = DataUtility.GetOuterUV(overrideSprite);
        float tx0 = outer.x;
        float ty0 = outer.y;
        float tx1 = outer.z;
        float ty1 = outer.w;

        if (fillMethod == FillMethod.Horizontal)
        {
            float fill = (tx1 - tx0) * segmentFillAmount;
            if (fillOrigin == (int)OriginHorizontal.Right)
            {
                v.x = v.z - (v.z - v.x) * segmentFillAmount;
                tx0 = tx1 - fill;
            }
            else
            {
                v.z = v.x + (v.z - v.x) * segmentFillAmount;
                tx1 = tx0 + fill;
            }
        }
        else if (fillMethod == FillMethod.Vertical)
        {
            float fill = (ty1 - ty0) * segmentFillAmount;
            if (fillOrigin == (int)OriginVertical.Top)
            {
                v.y = v.w - (v.w - v.y) * segmentFillAmount;
                ty0 = ty1 - fill;
            }
            else
            {
                v.w = v.y + (v.w - v.y) * segmentFillAmount;
                ty1 = ty0 + fill;
            }
        }

        s_Xy[0] = new Vector2(v.x, v.y);
        s_Xy[1] = new Vector2(v.x, v.w);
        s_Xy[2] = new Vector2(v.z, v.w);
        s_Xy[3] = new Vector2(v.z, v.y);

        s_Uv[0] = new Vector2(tx0, ty0);
        s_Uv[1] = new Vector2(tx0, ty1);
        s_Uv[2] = new Vector2(tx1, ty1);
        s_Uv[3] = new Vector2(tx1, ty0);

        if (segmentFillAmount < 1f && fillMethod != FillMethod.Horizontal && fillMethod != FillMethod.Vertical)
        {
            if (fillMethod == FillMethod.Radial90)
            {
                if (RadialCut(s_Xy, s_Uv, segmentFillAmount, fillClockwise, fillOrigin))
                    AddQuad(vh, s_Xy, color, s_Uv);
            }
            else if (fillMethod == FillMethod.Radial180)
            {
                for (int side = 0; side < 2; side++)
                {
                    float fx0;
                    float fx1;
                    float fy0;
                    float fy1;
                    int even = fillOrigin > 1 ? 1 : 0;

                    if (fillOrigin == 0 || fillOrigin == 2)
                    {
                        fy0 = 0f;
                        fy1 = 1f;
                        if (side == even)
                        {
                            fx0 = 0f;
                            fx1 = 0.5f;
                        }
                        else
                        {
                            fx0 = 0.5f;
                            fx1 = 1f;
                        }
                    }
                    else
                    {
                        fx0 = 0f;
                        fx1 = 1f;
                        if (side == even)
                        {
                            fy0 = 0.5f;
                            fy1 = 1f;
                        }
                        else
                        {
                            fy0 = 0f;
                            fy1 = 0.5f;
                        }
                    }

                    SetRadialQuad(v, tx0, tx1, ty0, ty1, fx0, fx1, fy0, fy1);
                    float val = fillClockwise ? segmentFillAmount * 2f - side : segmentFillAmount * 2f - (1 - side);

                    if (RadialCut(s_Xy, s_Uv, Mathf.Clamp01(val), fillClockwise, (side + fillOrigin + 3) % 4))
                        AddQuad(vh, s_Xy, color, s_Uv);
                }
            }
            else if (fillMethod == FillMethod.Radial360)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    float fx0 = corner < 2 ? 0f : 0.5f;
                    float fx1 = corner < 2 ? 0.5f : 1f;
                    float fy0 = corner == 0 || corner == 3 ? 0f : 0.5f;
                    float fy1 = corner == 0 || corner == 3 ? 0.5f : 1f;

                    SetRadialQuad(v, tx0, tx1, ty0, ty1, fx0, fx1, fy0, fy1);

                    float val = fillClockwise
                        ? segmentFillAmount * 4f - ((corner + fillOrigin) % 4)
                        : segmentFillAmount * 4f - (3 - ((corner + fillOrigin) % 4));

                    if (RadialCut(s_Xy, s_Uv, Mathf.Clamp01(val), fillClockwise, (corner + 2) % 4))
                        AddQuad(vh, s_Xy, color, s_Uv);
                }
            }
        }
        else
        {
            AddQuad(vh, s_Xy, color, s_Uv);
        }
    }

    private static void SetRadialQuad(Vector4 v, float tx0, float tx1, float ty0, float ty1, float fx0, float fx1, float fy0, float fy1)
    {
        s_Xy[0].x = Mathf.Lerp(v.x, v.z, fx0);
        s_Xy[1].x = s_Xy[0].x;
        s_Xy[2].x = Mathf.Lerp(v.x, v.z, fx1);
        s_Xy[3].x = s_Xy[2].x;

        s_Xy[0].y = Mathf.Lerp(v.y, v.w, fy0);
        s_Xy[1].y = Mathf.Lerp(v.y, v.w, fy1);
        s_Xy[2].y = s_Xy[1].y;
        s_Xy[3].y = s_Xy[0].y;

        s_Uv[0].x = Mathf.Lerp(tx0, tx1, fx0);
        s_Uv[1].x = s_Uv[0].x;
        s_Uv[2].x = Mathf.Lerp(tx0, tx1, fx1);
        s_Uv[3].x = s_Uv[2].x;

        s_Uv[0].y = Mathf.Lerp(ty0, ty1, fy0);
        s_Uv[1].y = Mathf.Lerp(ty0, ty1, fy1);
        s_Uv[2].y = s_Uv[1].y;
        s_Uv[3].y = s_Uv[0].y;
    }

    private Vector4 GetDrawingDimensions(Rect rect, bool shouldPreserveAspect)
    {
        Sprite activeSprite = overrideSprite;
        Vector4 padding = activeSprite == null ? Vector4.zero : DataUtility.GetPadding(activeSprite);
        Vector2 size = activeSprite == null ? Vector2.zero : activeSprite.rect.size;
        int spriteW = Mathf.RoundToInt(size.x);
        int spriteH = Mathf.RoundToInt(size.y);

        Vector4 v = spriteW > 0 && spriteH > 0
            ? new Vector4(
                padding.x / spriteW,
                padding.y / spriteH,
                (spriteW - padding.z) / spriteW,
                (spriteH - padding.w) / spriteH)
            : new Vector4(0f, 0f, 1f, 1f);

        if (shouldPreserveAspect && size.sqrMagnitude > 0f)
            PreserveSpriteAspectRatio(ref rect, size);

        return new Vector4(
            rect.x + rect.width * v.x,
            rect.y + rect.height * v.y,
            rect.x + rect.width * v.z,
            rect.y + rect.height * v.w);
    }

    private void PreserveSpriteAspectRatio(ref Rect rect, Vector2 spriteSize)
    {
        float spriteRatio = spriteSize.x / spriteSize.y;
        float rectRatio = rect.width / rect.height;

        if (spriteRatio > rectRatio)
        {
            float oldHeight = rect.height;
            rect.height = rect.width / spriteRatio;
            rect.y += (oldHeight - rect.height) * rectTransform.pivot.y;
        }
        else
        {
            float oldWidth = rect.width;
            rect.width = rect.height * spriteRatio;
            rect.x += (oldWidth - rect.width) * rectTransform.pivot.x;
        }
    }

    private Vector4 GetAdjustedBorders(Vector4 border, Rect adjustedRect)
    {
        Rect originalRect = rectTransform.rect;
        Rect pixelRect = GetPixelAdjustedRect();

        for (int axisIndex = 0; axisIndex <= 1; axisIndex++)
        {
            float borderScaleRatio = originalRect.size[axisIndex] != 0f
                ? pixelRect.size[axisIndex] / originalRect.size[axisIndex]
                : 1f;

            border[axisIndex] *= borderScaleRatio;
            border[axisIndex + 2] *= borderScaleRatio;

            float combinedBorders = border[axisIndex] + border[axisIndex + 2];
            if (adjustedRect.size[axisIndex] < combinedBorders && combinedBorders != 0f)
            {
                float fitScale = adjustedRect.size[axisIndex] / combinedBorders;
                border[axisIndex] *= fitScale;
                border[axisIndex + 2] *= fitScale;
            }
        }

        return border;
    }

    private static bool RadialCut(Vector3[] xy, Vector3[] uv, float fill, bool invert, int corner)
    {
        if (fill < 0.001f)
            return false;

        if ((corner & 1) == 1)
            invert = !invert;

        if (!invert && fill > 0.999f)
            return true;

        float angle = Mathf.Clamp01(fill);
        if (invert)
            angle = 1f - angle;

        angle *= 90f * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        RadialCut(xy, cos, sin, invert, corner);
        RadialCut(uv, cos, sin, invert, corner);
        return true;
    }

    private static void RadialCut(Vector3[] xy, float cos, float sin, bool invert, int corner)
    {
        int i0 = corner;
        int i1 = (corner + 1) % 4;
        int i2 = (corner + 2) % 4;
        int i3 = (corner + 3) % 4;

        if ((corner & 1) == 1)
        {
            if (sin > cos)
            {
                cos /= sin;
                sin = 1f;

                if (invert)
                {
                    xy[i1].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
                    xy[i2].x = xy[i1].x;
                }
            }
            else if (cos > sin)
            {
                sin /= cos;
                cos = 1f;

                if (!invert)
                {
                    xy[i2].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
                    xy[i3].y = xy[i2].y;
                }
            }
            else
            {
                cos = 1f;
                sin = 1f;
            }

            if (!invert)
                xy[i3].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
            else
                xy[i1].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
        }
        else
        {
            if (cos > sin)
            {
                sin /= cos;
                cos = 1f;

                if (!invert)
                {
                    xy[i1].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
                    xy[i2].y = xy[i1].y;
                }
            }
            else if (sin > cos)
            {
                cos /= sin;
                sin = 1f;

                if (invert)
                {
                    xy[i2].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
                    xy[i3].x = xy[i2].x;
                }
            }
            else
            {
                cos = 1f;
                sin = 1f;
            }

            if (invert)
                xy[i3].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
            else
                xy[i1].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
        }
    }

    private static void AddQuad(VertexHelper vertexHelper, Vector3[] positions, Color32 quadColor, Vector3[] uvs)
    {
        int start = vertexHelper.currentVertCount;

        for (int i = 0; i < 4; i++)
            vertexHelper.AddVert(positions[i], quadColor, uvs[i]);

        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start + 2, start + 3, start);
    }

    private static void AddQuad(VertexHelper vertexHelper, Vector2 posMin, Vector2 posMax, Color32 quadColor, Vector2 uvMin, Vector2 uvMax)
    {
        int start = vertexHelper.currentVertCount;

        vertexHelper.AddVert(new Vector3(posMin.x, posMin.y, 0f), quadColor, new Vector2(uvMin.x, uvMin.y));
        vertexHelper.AddVert(new Vector3(posMin.x, posMax.y, 0f), quadColor, new Vector2(uvMin.x, uvMax.y));
        vertexHelper.AddVert(new Vector3(posMax.x, posMax.y, 0f), quadColor, new Vector2(uvMax.x, uvMax.y));
        vertexHelper.AddVert(new Vector3(posMax.x, posMin.y, 0f), quadColor, new Vector2(uvMax.x, uvMin.y));

        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start + 2, start + 3, start);
    }

    private void EnsureDrawMaskSize(bool newBitsVisible)
    {
        int requiredWords = WordCountFor(Mathf.Max(1, m_SegmentCount));
        int oldLength = m_DrawMaskWords?.Length ?? 0;

        if (oldLength == requiredWords)
            return;

        int[] newWords = new int[requiredWords];
        if (m_DrawMaskWords != null)
            Array.Copy(m_DrawMaskWords, newWords, Math.Min(oldLength, requiredWords));

        if (newBitsVisible && requiredWords > oldLength)
        {
            for (int i = oldLength; i < requiredWords; i++)
                newWords[i] = -1;
        }

        m_DrawMaskWords = newWords;
    }

    private static int WordCountFor(int bitCount)
    {
        return (bitCount + 31) >> 5;
    }

    protected override void OnDidApplyAnimationProperties()
    {
        base.OnDidApplyAnimationProperties();
        m_SegmentCount = Mathf.Max(1, m_SegmentCount);
        m_Spacing = Mathf.Max(0f, m_Spacing);
        m_IntMaxValue = Mathf.Max(1, m_IntMaxValue);
        m_FloatMaxValue = Mathf.Max(0.0001f, m_FloatMaxValue);
        EnsureDrawMaskSize(true);
        SetAllDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        m_SegmentCount = Mathf.Max(1, m_SegmentCount);
        m_Spacing = Mathf.Max(0f, m_Spacing);
        m_IntMaxValue = Mathf.Max(1, m_IntMaxValue);
        m_FloatMaxValue = Mathf.Max(0.0001f, m_FloatMaxValue);
        EnsureDrawMaskSize(true);
        SetAllDirty();
    }
#endif
}

#if UNITY_EDITOR
namespace UnityEditor.UI
{
    [CustomEditor(typeof(SegmentedImage), true)]
    [CanEditMultipleObjects]
    public class SegmentedImageEditor : GraphicEditor
    {
        private SerializedProperty m_FillMethod;
        private SerializedProperty m_FillOrigin;
        private SerializedProperty m_FillAmount;
        private SerializedProperty m_FillClockwise;
        private SerializedProperty m_Type;
        private SerializedProperty m_FillCenter;
        private SerializedProperty m_Sprite;
        private SerializedProperty m_PreserveAspect;
        private SerializedProperty m_UseSpriteMesh;
        private SerializedProperty m_PixelsPerUnitMultiplier;
        private SerializedProperty m_SegmentCount;
        private SerializedProperty m_Spacing;
        private SerializedProperty m_Direction;
        private SerializedProperty m_DrawMaskWords;
        private SerializedProperty m_ValueMode;
        private SerializedProperty m_IntMaxValue;
        private SerializedProperty m_FloatMaxValue;
        private GUIContent m_SpriteContent;
        private GUIContent m_SpriteTypeContent;
        private GUIContent m_ClockwiseContent;
        private AnimBool m_ShowSlicedOrTiled;
        private AnimBool m_ShowSliced;
        private AnimBool m_ShowTiled;
        private AnimBool m_ShowFilled;
        private bool m_IsDriven;

        private static class Styles
        {
            public static readonly GUIContent FillOrigin = EditorGUIUtility.TrTextContent("Fill Origin");
            public static readonly GUIContent[] OriginHorizontal =
            {
                EditorGUIUtility.TrTextContent("Left"),
                EditorGUIUtility.TrTextContent("Right")
            };

            public static readonly GUIContent[] OriginVertical =
            {
                EditorGUIUtility.TrTextContent("Bottom"),
                EditorGUIUtility.TrTextContent("Top")
            };

            public static readonly GUIContent[] Origin90 =
            {
                EditorGUIUtility.TrTextContent("BottomLeft"),
                EditorGUIUtility.TrTextContent("TopLeft"),
                EditorGUIUtility.TrTextContent("TopRight"),
                EditorGUIUtility.TrTextContent("BottomRight")
            };

            public static readonly GUIContent[] Origin180 =
            {
                EditorGUIUtility.TrTextContent("Bottom"),
                EditorGUIUtility.TrTextContent("Left"),
                EditorGUIUtility.TrTextContent("Top"),
                EditorGUIUtility.TrTextContent("Right")
            };

            public static readonly GUIContent[] Origin360 =
            {
                EditorGUIUtility.TrTextContent("Bottom"),
                EditorGUIUtility.TrTextContent("Right"),
                EditorGUIUtility.TrTextContent("Top"),
                EditorGUIUtility.TrTextContent("Left")
            };
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            m_SpriteContent = EditorGUIUtility.TrTextContent("Source Image");
            m_SpriteTypeContent = EditorGUIUtility.TrTextContent("Image Type");
            m_ClockwiseContent = EditorGUIUtility.TrTextContent("Clockwise");

            m_Sprite = serializedObject.FindProperty("m_Sprite");
            m_Type = serializedObject.FindProperty("m_Type");
            m_FillCenter = serializedObject.FindProperty("m_FillCenter");
            m_FillMethod = serializedObject.FindProperty("m_FillMethod");
            m_FillOrigin = serializedObject.FindProperty("m_FillOrigin");
            m_FillClockwise = serializedObject.FindProperty("m_FillClockwise");
            m_FillAmount = serializedObject.FindProperty("m_FillAmount");
            m_PreserveAspect = serializedObject.FindProperty("m_PreserveAspect");
            m_UseSpriteMesh = serializedObject.FindProperty("m_UseSpriteMesh");
            m_PixelsPerUnitMultiplier = serializedObject.FindProperty("m_PixelsPerUnitMultiplier");
            m_SegmentCount = serializedObject.FindProperty("m_SegmentCount");
            m_Spacing = serializedObject.FindProperty("m_Spacing");
            m_Direction = serializedObject.FindProperty("m_Direction");
            m_DrawMaskWords = serializedObject.FindProperty("m_DrawMaskWords");
            m_ValueMode = serializedObject.FindProperty("m_ValueMode");
            m_IntMaxValue = serializedObject.FindProperty("m_IntMaxValue");
            m_FloatMaxValue = serializedObject.FindProperty("m_FloatMaxValue");

            var typeEnum = (Image.Type)m_Type.enumValueIndex;
            m_ShowSlicedOrTiled = new AnimBool(!m_Type.hasMultipleDifferentValues && (typeEnum == Image.Type.Sliced || typeEnum == Image.Type.Tiled));
            m_ShowSliced = new AnimBool(!m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Sliced);
            m_ShowTiled = new AnimBool(!m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Tiled);
            m_ShowFilled = new AnimBool(!m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Filled);
            m_ShowSlicedOrTiled.valueChanged.AddListener(Repaint);
            m_ShowSliced.valueChanged.AddListener(Repaint);
            m_ShowTiled.valueChanged.AddListener(Repaint);
            m_ShowFilled.valueChanged.AddListener(Repaint);

            SetShowNativeSize(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            m_ShowSlicedOrTiled.valueChanged.RemoveListener(Repaint);
            m_ShowSliced.valueChanged.RemoveListener(Repaint);
            m_ShowTiled.valueChanged.RemoveListener(Repaint);
            m_ShowFilled.valueChanged.RemoveListener(Repaint);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var image = (SegmentedImage)target;
            RectTransform rect = image.GetComponent<RectTransform>();
            m_IsDriven = (rect.drivenByObject as Slider)?.fillRect == rect;

            SpriteGUI();
            AppearanceControlsGUI();
            RaycastControlsGUI();
            MaskableControlsGUI();
            SegmentsGUI();

            TypeGUI();

            SetShowNativeSize(false);
            if (EditorGUILayout.BeginFadeGroup(m_ShowNativeSize.faded))
            {
                EditorGUI.indentLevel++;

                if ((Image.Type)m_Type.enumValueIndex == Image.Type.Simple)
                    EditorGUILayout.PropertyField(m_UseSpriteMesh);

                EditorGUILayout.PropertyField(m_PreserveAspect);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFadeGroup();

            NativeSizeButtonGUI();

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (UnityEngine.Object obj in targets)
                {
                    if (obj is SegmentedImage segmentedImage)
                        segmentedImage.SetAllDirty();
                }
            }
        }

        private void SegmentsGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Segments", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Direction, new GUIContent("Direction"));
            EditorGUILayout.PropertyField(m_SegmentCount, new GUIContent("Count"));
            EditorGUILayout.PropertyField(m_Spacing, new GUIContent("Spacing (px)"));
            DrawMaskGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel);
            DrawValueGUI();
        }

        private void SpriteGUI()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_Sprite, m_SpriteContent);
            if (!EditorGUI.EndChangeCheck())
                return;

            var newSprite = m_Sprite.objectReferenceValue as Sprite;
            if (newSprite)
            {
                Image.Type oldType = (Image.Type)m_Type.enumValueIndex;
                if (newSprite.border.sqrMagnitude > 0f)
                    m_Type.enumValueIndex = (int)Image.Type.Sliced;
                else if (oldType == Image.Type.Sliced)
                    m_Type.enumValueIndex = (int)Image.Type.Simple;
            }

            foreach (UnityEngine.Object obj in targets)
                ((Image)obj).DisableSpriteOptimizations();
        }

        private void TypeGUI()
        {
            EditorGUILayout.PropertyField(m_Type, m_SpriteTypeContent);
            EditorGUI.indentLevel++;

            Image.Type typeEnum = (Image.Type)m_Type.enumValueIndex;
            bool showSlicedOrTiled = !m_Type.hasMultipleDifferentValues && (typeEnum == Image.Type.Sliced || typeEnum == Image.Type.Tiled);

            if (showSlicedOrTiled)
                showSlicedOrTiled = AllSpritesHaveBorder();

            m_ShowSlicedOrTiled.target = showSlicedOrTiled;
            m_ShowSliced.target = showSlicedOrTiled && !m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Sliced;
            m_ShowTiled.target = showSlicedOrTiled && !m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Tiled;
            m_ShowFilled.target = !m_Type.hasMultipleDifferentValues && typeEnum == Image.Type.Filled;

            Sprite sprite = m_Sprite.hasMultipleDifferentValues ? null : m_Sprite.objectReferenceValue as Sprite;
            bool hasBorder = sprite != null && sprite.border.sqrMagnitude > 0f;

            if (EditorGUILayout.BeginFadeGroup(m_ShowSlicedOrTiled.faded))
            {
                if (hasBorder)
                    EditorGUILayout.PropertyField(m_FillCenter);
                EditorGUILayout.PropertyField(m_PixelsPerUnitMultiplier);
            }
            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(m_ShowSliced.faded))
            {
                if (sprite != null && !hasBorder)
                    EditorGUILayout.HelpBox("This Image doesn't have a border.", MessageType.Warning);
            }
            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(m_ShowTiled.faded))
            {
                if (sprite != null && !hasBorder &&
                    (sprite.texture != null && sprite.texture.wrapMode != TextureWrapMode.Repeat || sprite.packed))
                {
                    EditorGUILayout.HelpBox("It looks like you want to tile a sprite with no border. It would be more efficient to remove this Sprite from any SpriteAtlas and set the Wrap mode to Repeat.", MessageType.Warning);
                }
            }
            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(m_ShowFilled.faded))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(m_FillMethod);
                if (EditorGUI.EndChangeCheck())
                    m_FillOrigin.intValue = 0;

                Rect shapeRect = EditorGUILayout.GetControlRect(true);
                switch ((Image.FillMethod)m_FillMethod.enumValueIndex)
                {
                    case Image.FillMethod.Horizontal:
                        DrawFillOriginPopup(shapeRect, Styles.OriginHorizontal);
                        break;
                    case Image.FillMethod.Vertical:
                        DrawFillOriginPopup(shapeRect, Styles.OriginVertical);
                        break;
                    case Image.FillMethod.Radial90:
                        DrawFillOriginPopup(shapeRect, Styles.Origin90);
                        break;
                    case Image.FillMethod.Radial180:
                        DrawFillOriginPopup(shapeRect, Styles.Origin180);
                        break;
                    case Image.FillMethod.Radial360:
                        DrawFillOriginPopup(shapeRect, Styles.Origin360);
                        break;
                }

                if ((Image.FillMethod)m_FillMethod.enumValueIndex > Image.FillMethod.Vertical)
                    EditorGUILayout.PropertyField(m_FillClockwise, m_ClockwiseContent);
            }
            EditorGUILayout.EndFadeGroup();

            EditorGUI.indentLevel--;
        }

        private bool AllSpritesHaveBorder()
        {
            foreach (UnityEngine.Object obj in targets)
            {
                var targetSerializedObject = new SerializedObject(obj);
                SerializedProperty spriteProperty = targetSerializedObject.FindProperty("m_Sprite");
                Sprite sprite = spriteProperty.objectReferenceValue as Sprite;
                if (sprite == null || sprite.border.sqrMagnitude <= 0f)
                    return false;
            }

            return true;
        }

        private static readonly int[] s_OriginValues2 = { 0, 1 };
        private static readonly int[] s_OriginValues4 = { 0, 1, 2, 3 };

        private void DrawFillOriginPopup(Rect rect, GUIContent[] options)
        {
            int[] values = options.Length == 2 ? s_OriginValues2 : s_OriginValues4;
            EditorGUI.IntPopup(rect, m_FillOrigin, options, values, Styles.FillOrigin);
        }

        private void DrawValueGUI()
        {
            if (m_IsDriven)
                EditorGUILayout.HelpBox("The Fill Amount property is driven by Slider.", MessageType.None);

            using (new EditorGUI.DisabledScope(m_IsDriven))
                EditorGUILayout.PropertyField(m_FillAmount, new GUIContent("Fill Amount"));

            EditorGUILayout.PropertyField(m_ValueMode, new GUIContent("Value Type"));

            if (m_ValueMode.hasMultipleDifferentValues)
                return;

            if ((SegmentedImage.ValueMode)m_ValueMode.enumValueIndex == SegmentedImage.ValueMode.Int)
                DrawIntValueGUI();
            else
                DrawFloatValueGUI();
        }

        private void DrawIntValueGUI()
        {
            int oldMax = Mathf.Max(1, m_IntMaxValue.intValue);
            int currentValue = Mathf.Clamp(Mathf.RoundToInt(m_FillAmount.floatValue * oldMax), 0, oldMax);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_IntMaxValue, new GUIContent("Max Value"));
            if (EditorGUI.EndChangeCheck())
            {
                m_IntMaxValue.intValue = Mathf.Max(1, m_IntMaxValue.intValue);
                int newMax = m_IntMaxValue.intValue;
                m_FillAmount.floatValue = Mathf.Clamp(currentValue, 0, newMax) / (float)newMax;
            }

            int maxValue = Mathf.Max(1, m_IntMaxValue.intValue);
            DrawIntFillAmountSlider(m_FillAmount, maxValue, m_IsDriven);
        }

        private void DrawFloatValueGUI()
        {
            float oldMax = Mathf.Max(0.0001f, m_FloatMaxValue.floatValue);
            float currentValue = Mathf.Clamp(m_FillAmount.floatValue * oldMax, 0f, oldMax);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_FloatMaxValue, new GUIContent("Max Value"));
            if (EditorGUI.EndChangeCheck())
            {
                m_FloatMaxValue.floatValue = Mathf.Max(0.0001f, m_FloatMaxValue.floatValue);
                float newMax = m_FloatMaxValue.floatValue;
                m_FillAmount.floatValue = Mathf.Clamp(currentValue, 0f, newMax) / newMax;
            }

            float maxValue = Mathf.Max(0.0001f, m_FloatMaxValue.floatValue);
            DrawFloatFillAmountSlider(m_FillAmount, maxValue, m_IsDriven);
        }

        private static void DrawIntFillAmountSlider(SerializedProperty fillAmount, int maxValue, bool isDriven)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent label = EditorGUIUtility.TrTextContent("Value");
            EditorGUI.BeginProperty(rect, label, fillAmount);

            bool oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = fillAmount.hasMultipleDifferentValues;

            using (new EditorGUI.DisabledScope(isDriven))
            {
                int value = Mathf.Clamp(Mathf.RoundToInt(fillAmount.floatValue * maxValue), 0, maxValue);

                EditorGUI.BeginChangeCheck();
                int newValue = EditorGUI.IntSlider(rect, label, value, 0, maxValue);
                if (EditorGUI.EndChangeCheck())
                    fillAmount.floatValue = newValue / (float)maxValue;
            }

            EditorGUI.showMixedValue = oldMixedValue;
            EditorGUI.EndProperty();
        }

        private static void DrawFloatFillAmountSlider(SerializedProperty fillAmount, float maxValue, bool isDriven)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent label = EditorGUIUtility.TrTextContent("Value");
            EditorGUI.BeginProperty(rect, label, fillAmount);

            bool oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = fillAmount.hasMultipleDifferentValues;

            using (new EditorGUI.DisabledScope(isDriven))
            {
                float value = Mathf.Clamp(fillAmount.floatValue * maxValue, 0f, maxValue);

                EditorGUI.BeginChangeCheck();
                float newValue = EditorGUI.Slider(rect, label, value, 0f, maxValue);
                if (EditorGUI.EndChangeCheck())
                    fillAmount.floatValue = newValue / maxValue;
            }

            EditorGUI.showMixedValue = oldMixedValue;
            EditorGUI.EndProperty();
        }

        private void DrawMaskGUI()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Draw Mask can be edited with one SegmentedImage selected.", MessageType.None);
                return;
            }

            int count = Mathf.Max(1, m_SegmentCount.intValue);
            const float buttonWidth = 28f;
            float availableWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 42f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / buttonWidth));

            EditorGUILayout.LabelField("Draw Mask");

            for (int rowStart = 0; rowStart < count; rowStart += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                int rowEnd = Mathf.Min(count, rowStart + perRow);

                for (int i = rowStart; i < rowEnd; i++)
                {
                    bool visible = GetMaskBit(i);
                    bool newVisible = GUILayout.Toggle(visible, i.ToString(), EditorStyles.miniButton, GUILayout.Width(buttonWidth));
                    if (newVisible != visible)
                        SetMaskBit(i, newVisible);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All"))
                SetAllMaskBits(count, true);
            if (GUILayout.Button("None"))
                SetAllMaskBits(count, false);
            EditorGUILayout.EndHorizontal();
        }

        private bool GetMaskBit(int index)
        {
            int wordIndex = index >> 5;
            if (wordIndex >= m_DrawMaskWords.arraySize)
                return true;

            int word = m_DrawMaskWords.GetArrayElementAtIndex(wordIndex).intValue;
            return (word & (1 << (index & 31))) != 0;
        }

        private void SetMaskBit(int index, bool value)
        {
            int wordIndex = index >> 5;
            while (m_DrawMaskWords.arraySize <= wordIndex)
            {
                int newIndex = m_DrawMaskWords.arraySize;
                m_DrawMaskWords.arraySize = newIndex + 1;
                m_DrawMaskWords.GetArrayElementAtIndex(newIndex).intValue = -1;
            }

            SerializedProperty wordProperty = m_DrawMaskWords.GetArrayElementAtIndex(wordIndex);
            int mask = 1 << (index & 31);
            int word = wordProperty.intValue;
            wordProperty.intValue = value ? word | mask : word & ~mask;
        }

        private void SetAllMaskBits(int count, bool value)
        {
            int wordCount = (count + 31) >> 5;
            m_DrawMaskWords.arraySize = wordCount;
            for (int i = 0; i < wordCount; i++)
                m_DrawMaskWords.GetArrayElementAtIndex(i).intValue = value ? -1 : 0;
        }

        private void SetShowNativeSize(bool instant)
        {
            Image.Type type = (Image.Type)m_Type.enumValueIndex;
            bool showNativeSize = (type == Image.Type.Simple || type == Image.Type.Filled) && m_Sprite.objectReferenceValue != null;
            base.SetShowNativeSize(showNativeSize, instant);
        }

    }
}
#endif
