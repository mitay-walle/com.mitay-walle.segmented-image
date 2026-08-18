using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AnimatedValues;
#endif

public enum ValueMode
{
    Int,
    Float
}

[Icon("Image Icon")]
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI (Canvas)/Segmented Image", 12)]
public class SegmentedImage : Image
{
    private static readonly Vector2[] VertScratch = new Vector2[4];
    private static readonly Vector2[] UVScratch = new Vector2[4];
    private static readonly Vector3[] XY = new Vector3[4];
    private static readonly Vector3[] UV = new Vector3[4];

    [FormerlySerializedAs("m_SegmentCount")]
    [SerializeField] [Min(1)] private int _mSegmentCount = 5;
    [FormerlySerializedAs("m_Spacing")]
    [SerializeField] [Min(0f)] private float _mSpacing = 2f;
    [FormerlySerializedAs("m_Direction")]
    [SerializeField] private Slider.Direction _mDirection = Slider.Direction.LeftToRight;
    [FormerlySerializedAs("m_EmptyColor")]
    [SerializeField] private Color _mEmptyColor = Color.clear;
    [FormerlySerializedAs("m_IsHandle")]
    [SerializeField] private bool _mIsHandle;
    [FormerlySerializedAs("m_DrawMaskWords")]
    [SerializeField] private int[] _mDrawMaskWords =
    {
        -1
    };
    [FormerlySerializedAs("m_ValueMode")]
    [SerializeField] private ValueMode _mValueMode = ValueMode.Float;
    [SerializeField] [HideInInspector] [FormerlySerializedAs("m_Axis")] private RectTransform.Axis _mLegacyAxis = RectTransform.Axis.Horizontal;
    [SerializeField] [HideInInspector] [FormerlySerializedAs("m_FillInvert")] private bool _mLegacyFillInvert;
    [FormerlySerializedAs("m_SerializationVersion")]
    [SerializeField] [HideInInspector] private int _mSerializationVersion;
    [FormerlySerializedAs("m_IntMaxValue")]
    [SerializeField] [Min(1)] private int _mIntMaxValue = 5;
    [FormerlySerializedAs("m_FloatMaxValue")]
    [SerializeField] [Min(0.0001f)] private float _mFloatMaxValue = 1f;

    public int SegmentCount
    {
        get => _mSegmentCount;
        set
        {
            value = Mathf.Max(1, value);

            if (_mSegmentCount == value)
            {
                return;
            }

            _mSegmentCount = value;
            EnsureDrawMaskSize(true);
            SetAllDirty();
        }
    }

    public float Spacing
    {
        get => _mSpacing;
        set
        {
            value = Mathf.Max(0f, value);

            if (Mathf.Approximately(_mSpacing, value))
            {
                return;
            }

            _mSpacing = value;
            SetAllDirty();
        }
    }

    public Slider.Direction Direction
    {
        get => _mDirection;
        set
        {
            if (_mDirection == value)
            {
                return;
            }

            _mDirection = value;
            SetAllDirty();
        }
    }

    public RectTransform.Axis Axis
    {
        get => IsHorizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
        set => Direction = value == RectTransform.Axis.Horizontal
                               ? Slider.Direction.LeftToRight
                               : Slider.Direction.BottomToTop;
    }

    public Color EmptyColor
    {
        get => _mEmptyColor;
        set
        {
            if (_mEmptyColor == value)
            {
                return;
            }

            _mEmptyColor = value;
            SetAllDirty();
        }
    }

    public bool IsHandle
    {
        get => _mIsHandle;
        set
        {
            if (_mIsHandle == value)
            {
                return;
            }

            _mIsHandle = value;
            SetAllDirty();
        }
    }

    public ValueMode ValueMode
    {
        get => _mValueMode;
        set
        {
            if (_mValueMode == value)
            {
                return;
            }

            _mValueMode = value;
            SetAllDirty();
        }
    }

    public int INTMaxValue
    {
        get => _mIntMaxValue;
        set
        {
            int newMaxValue = Mathf.Max(1, value);

            if (_mIntMaxValue == newMaxValue)
            {
                return;
            }

            int currentValue = INTValue;
            _mIntMaxValue = newMaxValue;
            fillAmount = Mathf.Clamp(currentValue, 0, _mIntMaxValue) / (float)_mIntMaxValue;
            SetAllDirty();
        }
    }

    public int INTValue
    {
        get => Mathf.Clamp(Mathf.RoundToInt(fillAmount * _mIntMaxValue), 0, _mIntMaxValue);
        set
        {
            int maxValue = Mathf.Max(1, _mIntMaxValue);
            float normalizedValue = Mathf.Clamp(value, 0, maxValue) / (float)maxValue;

            if (Mathf.Approximately(fillAmount, normalizedValue))
            {
                return;
            }

            fillAmount = normalizedValue;
            SetAllDirty();
        }
    }

    public float FloatMaxValue
    {
        get => _mFloatMaxValue;
        set
        {
            float newMaxValue = Mathf.Max(0.0001f, value);

            if (Mathf.Approximately(_mFloatMaxValue, newMaxValue))
            {
                return;
            }

            float currentValue = FloatValue;
            _mFloatMaxValue = newMaxValue;
            fillAmount = Mathf.Clamp(currentValue, 0f, _mFloatMaxValue) / _mFloatMaxValue;
            SetAllDirty();
        }
    }

    public float FloatValue
    {
        get => fillAmount * _mFloatMaxValue;
        set
        {
            float maxValue = Mathf.Max(0.0001f, _mFloatMaxValue);
            float normalizedValue = Mathf.Clamp(value, 0f, maxValue) / maxValue;

            if (Mathf.Approximately(fillAmount, normalizedValue))
            {
                return;
            }

            fillAmount = normalizedValue;
            SetAllDirty();
        }
    }

    public BitArray DrawMask
    {
        get
        {
            EnsureDrawMaskSize(false);

            BitArray result = new(_mDrawMaskWords)
            {
                Length = _mSegmentCount
            };
            return result;
        }
        set
        {
            EnsureDrawMaskSize(false);

            BitArray mask = value == null ? new BitArray(_mSegmentCount, true) : new BitArray(value);
            mask.Length = _mSegmentCount;
            int[] words = new int[WordCountFor(_mSegmentCount)];
            mask.CopyTo(words, 0);
            _mDrawMaskWords = words;
            SetAllDirty();
        }
    }

    public override float preferredWidth
    {
        get
        {
            Sprite activeSprite = overrideSprite;

            if (activeSprite == null)
            {
                return 0f;
            }

            float width = type == Type.Sliced || type == Type.Tiled
                              ? DataUtility.GetMinSize(activeSprite).x / pixelsPerUnit
                              : activeSprite.rect.size.x / pixelsPerUnit;

            if (!IsHorizontal)
            {
                return width;
            }

            return width * _mSegmentCount + _mSpacing * Mathf.Max(0, _mSegmentCount - 1);
        }
    }

    public override float preferredHeight
    {
        get
        {
            Sprite activeSprite = overrideSprite;

            if (activeSprite == null)
            {
                return 0f;
            }

            float height = type == Type.Sliced || type == Type.Tiled
                               ? DataUtility.GetMinSize(activeSprite).y / pixelsPerUnit
                               : activeSprite.rect.size.y / pixelsPerUnit;

            if (IsHorizontal)
            {
                return height;
            }

            return height * _mSegmentCount + _mSpacing * Mathf.Max(0, _mSegmentCount - 1);
        }
    }

    private bool IsHorizontal =>
        _mDirection == Slider.Direction.LeftToRight ||
        _mDirection == Slider.Direction.RightToLeft;

    private bool IsFillSequenceReversed =>
        _mDirection == Slider.Direction.RightToLeft ||
        _mDirection == Slider.Direction.TopToBottom;

    protected override void OnEnable()
    {
        EnsureDrawMaskSize(true);
        base.OnEnable();
    }

    protected override void OnDidApplyAnimationProperties()
    {
        base.OnDidApplyAnimationProperties();
        _mSegmentCount = Mathf.Max(1, _mSegmentCount);
        _mSpacing = Mathf.Max(0f, _mSpacing);
        _mIntMaxValue = Mathf.Max(1, _mIntMaxValue);
        _mFloatMaxValue = Mathf.Max(0.0001f, _mFloatMaxValue);
        EnsureDrawMaskSize(true);
        SetAllDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        _mSegmentCount = Mathf.Max(1, _mSegmentCount);
        _mSpacing = Mathf.Max(0f, _mSpacing);
        _mIntMaxValue = Mathf.Max(1, _mIntMaxValue);
        _mFloatMaxValue = Mathf.Max(0.0001f, _mFloatMaxValue);
        EnsureDrawMaskSize(true);
        SetAllDirty();
    }
#endif

    public bool GetSegmentVisible(int index)
    {
        if ((uint)index >= (uint)_mSegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        EnsureDrawMaskSize(false);
        int word = index >> 5;
        int bit = index & 31;
        return (_mDrawMaskWords[word] & (1 << bit)) != 0;
    }

    public void SetSegmentVisible(int index, bool visible)
    {
        if ((uint)index >= (uint)_mSegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        EnsureDrawMaskSize(false);
        int word = index >> 5;
        int bit = index & 31;
        int mask = 1 << bit;

        if (visible)
        {
            _mDrawMaskWords[word] |= mask;
        }
        else
        {
            _mDrawMaskWords[word] &= ~mask;
        }

        SetAllDirty();
    }

    public void SetValue(int value, int maxValue)
    {
        ValueMode = ValueMode.Int;
        INTMaxValue = maxValue;
        INTValue = value;
    }

    public void SetValue(float value, float maxValue)
    {
        ValueMode = ValueMode.Float;
        FloatMaxValue = maxValue;
        FloatValue = value;
    }

    public override void SetNativeSize()
    {
        Sprite activeSprite = overrideSprite;

        if (activeSprite == null)
        {
            return;
        }

        float width = activeSprite.rect.width / pixelsPerUnit;
        float height = activeSprite.rect.height / pixelsPerUnit;
        float gaps = _mSpacing * Mathf.Max(0, _mSegmentCount - 1);

        if (IsHorizontal)
        {
            width = width * _mSegmentCount + gaps;
        }
        else
        {
            height = height * _mSegmentCount + gaps;
        }

        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = new Vector2(width, height);
        SetAllDirty();
    }

    public override void OnBeforeSerialize()
    {
        base.OnBeforeSerialize();
        _mSerializationVersion = 1;
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();

        if (_mSerializationVersion == 0)
        {
            _mDirection = _mLegacyAxis == RectTransform.Axis.Horizontal
                              ? (_mLegacyFillInvert ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight)
                              : (_mLegacyFillInvert ? Slider.Direction.TopToBottom : Slider.Direction.BottomToTop);
            _mSerializationVersion = 1;
        }

        _mSegmentCount = Mathf.Max(1, _mSegmentCount);
        _mSpacing = Mathf.Max(0f, _mSpacing);
        _mIntMaxValue = Mathf.Max(1, _mIntMaxValue);
        _mFloatMaxValue = Mathf.Max(0.0001f, _mFloatMaxValue);
        EnsureDrawMaskSize(true);
    }

    protected override void OnPopulateMesh(VertexHelper toFill)
    {
        toFill.Clear();

        int count = Mathf.Max(1, _mSegmentCount);
        Rect fullRect = GetPixelAdjustedRect();
        float totalSpacing = _mSpacing * Mathf.Max(0, count - 1);

        float segmentSize = IsHorizontal
                                ? (fullRect.width - totalSpacing) / count
                                : (fullRect.height - totalSpacing) / count;

        if (segmentSize <= 0f)
        {
            return;
        }

        EnsureDrawMaskSize(false);

        if (_mIsHandle)
        {
            GenerateHandle(toFill, fullRect, count, segmentSize);
            return;
        }

        bool reverseSequence = IsFillSequenceReversed;
        float totalFill = Mathf.Clamp01(fillAmount) * count;

        for (int slot = 0; slot < count; slot++)
        {
            if (!GetSegmentVisibleUnchecked(slot))
            {
                continue;
            }

            Rect segmentRect = GetSegmentRect(fullRect, slot, segmentSize);
            int fillIndex = reverseSequence ? count - 1 - slot : slot;
            float segmentFill = Mathf.Clamp01(totalFill - fillIndex);

            if (type == Type.Filled)
            {
                if (segmentFill <= 0.000001f)
                {
                    if (_mEmptyColor.a > 0f)
                    {
                        GenerateSegment(toFill, segmentRect, count, 1f, _mEmptyColor);
                    }

                    continue;
                }

                if (segmentFill >= 0.999999f)
                {
                    GenerateSegment(toFill, segmentRect, count, 1f, color);
                    continue;
                }

                if (_mEmptyColor.a > 0f)
                {
                    GenerateFilledComplement(toFill, segmentRect, segmentFill, _mEmptyColor);
                }

                GenerateSegment(toFill, segmentRect, count, segmentFill, color);
            }
            else
            {
                Color segmentColor = segmentFill >= 0.999999f ? color : _mEmptyColor;

                if (segmentColor.a > 0f)
                {
                    GenerateSegment(toFill, segmentRect, count, 1f, segmentColor);
                }
            }
        }
    }

    private bool GetSegmentVisibleUnchecked(int index)
    {
        int word = index >> 5;
        int bit = index & 31;
        return (_mDrawMaskWords[word] & (1 << bit)) != 0;
    }

    private void GenerateHandle(VertexHelper vh, Rect fullRect, int count, float segmentSize)
    {
        float totalFill = Mathf.Clamp01(fillAmount) * count;
        int fillIndex = Mathf.Clamp(Mathf.CeilToInt(totalFill) - 1, 0, count - 1);
        float segmentFill = Mathf.Clamp01(totalFill - fillIndex);
        int handleSlot = IsFillSequenceReversed ? count - 1 - fillIndex : fillIndex;
        bool drawEmpty = _mEmptyColor.a > 0f;

        for (int slot = 0; slot < count; slot++)
        {
            if (!GetSegmentVisibleUnchecked(slot))
            {
                continue;
            }

            Rect segmentRect = GetSegmentRect(fullRect, slot, segmentSize);

            if (slot != handleSlot)
            {
                if (drawEmpty)
                {
                    GenerateSegment(vh, segmentRect, count, 1f, _mEmptyColor);
                }

                continue;
            }

            if (type != Type.Filled)
            {
                GenerateSegment(vh, segmentRect, count, 1f, color);
                continue;
            }

            if (segmentFill <= 0.000001f)
            {
                if (drawEmpty)
                {
                    GenerateSegment(vh, segmentRect, count, 1f, _mEmptyColor);
                }

                continue;
            }

            if (segmentFill >= 0.999999f)
            {
                GenerateSegment(vh, segmentRect, count, 1f, color);
                continue;
            }

            if (drawEmpty)
            {
                GenerateFilledComplement(vh, segmentRect, segmentFill, _mEmptyColor);
            }

            GenerateSegment(vh, segmentRect, count, segmentFill, color);
        }
    }

    private void GenerateSegment(VertexHelper vh, Rect rect, int segmentCountForBudget, float segmentFillAmount, Color32 renderColor)
    {
        Sprite activeSprite = overrideSprite;

        if (activeSprite == null)
        {
            if (type == Type.Filled)
            {
                GenerateFilledSprite(vh, rect, preserveAspect, segmentFillAmount, renderColor);
            }
            else
            {
                GenerateSimpleSprite(vh, rect, preserveAspect, renderColor);
            }

            return;
        }

        switch (type)
        {
            case Type.Simple:
                if (useSpriteMesh)
                {
                    GenerateSprite(vh, rect, preserveAspect, renderColor);
                }
                else
                {
                    GenerateSimpleSprite(vh, rect, preserveAspect, renderColor);
                }

                break;
            case Type.Sliced:
                GenerateSlicedSprite(vh, rect, renderColor);
                break;
            case Type.Tiled:
                GenerateTiledSprite(vh, rect, segmentCountForBudget, renderColor);
                break;
            case Type.Filled:
                GenerateFilledSprite(vh, rect, preserveAspect, segmentFillAmount, renderColor);
                break;
        }
    }

    private Rect GetSegmentRect(Rect fullRect, int index, float segmentSize)
    {
        if (IsHorizontal)
        {
            float x = fullRect.x + index * (segmentSize + _mSpacing);
            return new Rect(x, fullRect.y, segmentSize, fullRect.height);
        }

        float y = fullRect.y + index * (segmentSize + _mSpacing);
        return new Rect(fullRect.x, y, fullRect.width, segmentSize);
    }

    private void GenerateSimpleSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect, Color32 renderColor)
    {
        Vector4 v = GetDrawingDimensions(rect, shouldPreserveAspect);
        Sprite activeSprite = overrideSprite;
        Vector4 uv = activeSprite != null ? DataUtility.GetOuterUV(activeSprite) : new Vector4(0f, 0f, 1f, 1f);
        Color32 color32 = renderColor;

        vh.AddVert(new Vector3(v.x, v.y), color32, new Vector2(uv.x, uv.y));
        vh.AddVert(new Vector3(v.x, v.w), color32, new Vector2(uv.x, uv.w));
        vh.AddVert(new Vector3(v.z, v.w), color32, new Vector2(uv.z, uv.w));
        vh.AddVert(new Vector3(v.z, v.y), color32, new Vector2(uv.z, uv.y));

        int start = vh.currentVertCount - 4;
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    private void GenerateSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect, Color32 renderColor)
    {
        Sprite activeSprite = overrideSprite;
        Vector2 spriteSize = activeSprite.rect.size;
        Vector2 spritePivot = activeSprite.pivot / spriteSize;
        Vector2 rectPivot = rectTransform.pivot;
        Vector2 pivotPosition = rect.position + Vector2.Scale(rectPivot, rect.size);

        if (shouldPreserveAspect && spriteSize.sqrMagnitude > 0f)
        {
            PreserveSpriteAspectRatio(ref rect, spriteSize);
        }

        Vector2 drawingSize = rect.size;
        Vector2 spriteBoundSize = activeSprite.bounds.size;
        Vector2 drawOffset = (rectPivot - spritePivot) * drawingSize;
        Color32 color32 = renderColor;
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
        {
            vh.AddTriangle(start + triangles[i], start + triangles[i + 1], start + triangles[i + 2]);
        }
    }

    private void GenerateSlicedSprite(VertexHelper vh, Rect rect, Color32 renderColor)
    {
        if (!hasBorder)
        {
            GenerateSimpleSprite(vh, rect, false, renderColor);
            return;
        }

        Sprite activeSprite = overrideSprite;
        Vector4 outer = DataUtility.GetOuterUV(activeSprite);
        Vector4 inner = DataUtility.GetInnerUV(activeSprite);
        Vector4 padding = DataUtility.GetPadding(activeSprite);
        Vector4 border = activeSprite.border;
        Vector4 adjustedBorders = GetAdjustedBorders(border / multipliedPixelsPerUnit, rect);
        padding /= multipliedPixelsPerUnit;

        VertScratch[0] = new Vector2(padding.x, padding.y);
        VertScratch[3] = new Vector2(rect.width - padding.z, rect.height - padding.w);
        VertScratch[1] = new Vector2(adjustedBorders.x, adjustedBorders.y);
        VertScratch[2] = new Vector2(rect.width - adjustedBorders.z, rect.height - adjustedBorders.w);

        for (int i = 0; i < 4; i++)
        {
            VertScratch[i] += rect.position;
        }

        UVScratch[0] = new Vector2(outer.x, outer.y);
        UVScratch[1] = new Vector2(inner.x, inner.y);
        UVScratch[2] = new Vector2(inner.z, inner.w);
        UVScratch[3] = new Vector2(outer.z, outer.w);

        for (int x = 0; x < 3; x++)
        {
            int x2 = x + 1;

            for (int y = 0; y < 3; y++)
            {
                if (!fillCenter && x == 1 && y == 1)
                {
                    continue;
                }

                int y2 = y + 1;

                if (VertScratch[x2].x - VertScratch[x].x <= 0f || VertScratch[y2].y - VertScratch[y].y <= 0f)
                {
                    continue;
                }

                AddQuad(
                        vh,
                        new Vector2(VertScratch[x].x, VertScratch[y].y),
                        new Vector2(VertScratch[x2].x, VertScratch[y2].y),
                        renderColor,
                        new Vector2(UVScratch[x].x, UVScratch[y].y),
                        new Vector2(UVScratch[x2].x, UVScratch[y2].y));
            }
        }
    }

    private void GenerateTiledSprite(VertexHelper vh, Rect rect, int segmentCountForBudget, Color32 renderColor)
    {
        Sprite activeSprite = overrideSprite;
        Vector4 outer = DataUtility.GetOuterUV(activeSprite);
        Vector4 inner = DataUtility.GetInnerUV(activeSprite);
        Vector4 border = activeSprite.border;
        Vector2 spriteSize = activeSprite.rect.size;
        float tileWidth = (spriteSize.x - border.x - border.z) / multipliedPixelsPerUnit;
        float tileHeight = (spriteSize.y - border.y - border.w) / multipliedPixelsPerUnit;

        border = GetAdjustedBorders(border / multipliedPixelsPerUnit, rect);

        Vector2 uvMin = new(inner.x, inner.y);
        Vector2 uvMax = new(inner.z, inner.w);
        float xMin = border.x;
        float xMax = rect.width - border.z;
        float yMin = border.y;
        float yMax = rect.height - border.w;
        Vector2 clipped = uvMax;

        if (tileWidth <= 0f)
        {
            tileWidth = xMax - xMin;
        }

        if (tileHeight <= 0f)
        {
            tileHeight = yMax - yMin;
        }

        if (activeSprite != null && (hasBorder || activeSprite.packed ||
                                     (activeSprite.texture != null && activeSprite.texture.wrapMode != TextureWrapMode.Repeat)))
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

                        AddQuad(vh, new Vector2(x1, y1) + rect.position, new Vector2(x2, y2) + rect.position, renderColor, uvMin, clipped);
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

                    AddQuad(vh, new Vector2(0f, y1) + rect.position, new Vector2(xMin, y2) + rect.position, renderColor,
                            new Vector2(outer.x, uvMin.y), new Vector2(uvMin.x, clipped.y));

                    AddQuad(vh, new Vector2(xMax, y1) + rect.position, new Vector2(rect.width, y2) + rect.position, renderColor,
                            new Vector2(uvMax.x, uvMin.y), new Vector2(outer.z, clipped.y));
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

                    AddQuad(vh, new Vector2(x1, 0f) + rect.position, new Vector2(x2, yMin) + rect.position, renderColor,
                            new Vector2(uvMin.x, outer.y), new Vector2(clipped.x, uvMin.y));

                    AddQuad(vh, new Vector2(x1, yMax) + rect.position, new Vector2(x2, rect.height) + rect.position, renderColor,
                            new Vector2(uvMin.x, uvMax.y), new Vector2(clipped.x, outer.w));
                }

                AddQuad(vh, new Vector2(0f, 0f) + rect.position, new Vector2(xMin, yMin) + rect.position, renderColor, new Vector2(outer.x, outer.y),
                        new Vector2(uvMin.x, uvMin.y));

                AddQuad(vh, new Vector2(xMax, 0f) + rect.position, new Vector2(rect.width, yMin) + rect.position, renderColor,
                        new Vector2(uvMax.x, outer.y), new Vector2(outer.z, uvMin.y));

                AddQuad(vh, new Vector2(0f, yMax) + rect.position, new Vector2(xMin, rect.height) + rect.position, renderColor,
                        new Vector2(outer.x, uvMax.y), new Vector2(uvMin.x, outer.w));

                AddQuad(vh, new Vector2(xMax, yMax) + rect.position, new Vector2(rect.width, rect.height) + rect.position, renderColor,
                        new Vector2(uvMax.x, uvMax.y), new Vector2(outer.z, outer.w));
            }
        }
        else
        {
            Vector2 uvScale = new((xMax - xMin) / tileWidth, (yMax - yMin) / tileHeight);

            if (fillCenter)
            {
                AddQuad(vh, new Vector2(xMin, yMin) + rect.position, new Vector2(xMax, yMax) + rect.position, renderColor,
                        Vector2.Scale(uvMin, uvScale), Vector2.Scale(uvMax, uvScale));
            }
        }
    }

    private void GenerateFilledComplement(VertexHelper vh, Rect rect, float filledAmount, Color32 renderColor)
    {
        float emptyAmount = 1f - filledAmount;

        if (emptyAmount <= 0.000001f)
        {
            return;
        }

        int complementOrigin = fillOrigin;
        bool complementClockwise = fillClockwise;

        if (fillMethod == FillMethod.Horizontal)
        {
            complementOrigin = fillOrigin == (int)OriginHorizontal.Left ? (int)OriginHorizontal.Right : (int)OriginHorizontal.Left;
        }
        else if (fillMethod == FillMethod.Vertical)
        {
            complementOrigin = fillOrigin == (int)OriginVertical.Bottom ? (int)OriginVertical.Top : (int)OriginVertical.Bottom;
        }
        else
        {
            complementClockwise = !fillClockwise;
        }

        GenerateFilledSprite(vh, rect, preserveAspect, emptyAmount, renderColor, complementOrigin, complementClockwise);
    }

    private void GenerateFilledSprite(VertexHelper vh, Rect rect, bool shouldPreserveAspect, float segmentFillAmount, Color32 renderColor)
    {
        GenerateFilledSprite(vh, rect, shouldPreserveAspect, segmentFillAmount, renderColor, fillOrigin, fillClockwise);
    }

    private void GenerateFilledSprite(
        VertexHelper vh,
        Rect rect,
        bool shouldPreserveAspect,
        float segmentFillAmount,
        Color32 renderColor,
        int segmentFillOrigin,
        bool segmentFillClockwise)
    {
        if (segmentFillAmount < 0.001f)
        {
            return;
        }

        Vector4 v = GetDrawingDimensions(rect, shouldPreserveAspect);
        Sprite activeSprite = overrideSprite;
        Vector4 outer = activeSprite != null ? DataUtility.GetOuterUV(activeSprite) : new Vector4(0f, 0f, 1f, 1f);
        float tx0 = outer.x;
        float ty0 = outer.y;
        float tx1 = outer.z;
        float ty1 = outer.w;

        if (fillMethod == FillMethod.Horizontal)
        {
            float fill = (tx1 - tx0) * segmentFillAmount;

            if (segmentFillOrigin == (int)OriginHorizontal.Right)
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

            if (segmentFillOrigin == (int)OriginVertical.Top)
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

        XY[0] = new Vector2(v.x, v.y);
        XY[1] = new Vector2(v.x, v.w);
        XY[2] = new Vector2(v.z, v.w);
        XY[3] = new Vector2(v.z, v.y);

        UV[0] = new Vector2(tx0, ty0);
        UV[1] = new Vector2(tx0, ty1);
        UV[2] = new Vector2(tx1, ty1);
        UV[3] = new Vector2(tx1, ty0);

        if (segmentFillAmount < 1f && fillMethod != FillMethod.Horizontal && fillMethod != FillMethod.Vertical)
        {
            if (fillMethod == FillMethod.Radial90)
            {
                if (RadialCut(XY, UV, segmentFillAmount, segmentFillClockwise, segmentFillOrigin))
                {
                    AddQuad(vh, XY, renderColor, UV);
                }
            }
            else if (fillMethod == FillMethod.Radial180)
            {
                for (int side = 0; side < 2; side++)
                {
                    float fx0;
                    float fx1;
                    float fy0;
                    float fy1;
                    int even = segmentFillOrigin > 1 ? 1 : 0;

                    if (segmentFillOrigin == 0 || segmentFillOrigin == 2)
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
                    float val = segmentFillClockwise ? segmentFillAmount * 2f - side : segmentFillAmount * 2f - (1 - side);

                    if (RadialCut(XY, UV, Mathf.Clamp01(val), segmentFillClockwise, (side + segmentFillOrigin + 3) % 4))
                    {
                        AddQuad(vh, XY, renderColor, UV);
                    }
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

                    float val = segmentFillClockwise
                                    ? segmentFillAmount * 4f - ((corner + segmentFillOrigin) % 4)
                                    : segmentFillAmount * 4f - (3 - ((corner + segmentFillOrigin) % 4));

                    if (RadialCut(XY, UV, Mathf.Clamp01(val), segmentFillClockwise, (corner + 2) % 4))
                    {
                        AddQuad(vh, XY, renderColor, UV);
                    }
                }
            }
        }
        else
        {
            AddQuad(vh, XY, renderColor, UV);
        }
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
        {
            PreserveSpriteAspectRatio(ref rect, size);
        }

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

    private void EnsureDrawMaskSize(bool newBitsVisible)
    {
        int requiredWords = WordCountFor(Mathf.Max(1, _mSegmentCount));
        int oldLength = _mDrawMaskWords?.Length ?? 0;

        if (oldLength == requiredWords)
        {
            return;
        }

        int[] newWords = new int[requiredWords];

        if (_mDrawMaskWords != null)
        {
            Array.Copy(_mDrawMaskWords, newWords, Math.Min(oldLength, requiredWords));
        }

        if (newBitsVisible && requiredWords > oldLength)
        {
            for (int i = oldLength; i < requiredWords; i++)
            {
                newWords[i] = -1;
            }
        }

        _mDrawMaskWords = newWords;
    }

    private static void SetRadialQuad(Vector4 v, float tx0, float tx1, float ty0, float ty1, float fx0, float fx1, float fy0, float fy1)
    {
        XY[0].x = Mathf.Lerp(v.x, v.z, fx0);
        XY[1].x = XY[0].x;
        XY[2].x = Mathf.Lerp(v.x, v.z, fx1);
        XY[3].x = XY[2].x;

        XY[0].y = Mathf.Lerp(v.y, v.w, fy0);
        XY[1].y = Mathf.Lerp(v.y, v.w, fy1);
        XY[2].y = XY[1].y;
        XY[3].y = XY[0].y;

        UV[0].x = Mathf.Lerp(tx0, tx1, fx0);
        UV[1].x = UV[0].x;
        UV[2].x = Mathf.Lerp(tx0, tx1, fx1);
        UV[3].x = UV[2].x;

        UV[0].y = Mathf.Lerp(ty0, ty1, fy0);
        UV[1].y = Mathf.Lerp(ty0, ty1, fy1);
        UV[2].y = UV[1].y;
        UV[3].y = UV[0].y;
    }

    private static bool RadialCut(Vector3[] xy, Vector3[] uv, float fill, bool invert, int corner)
    {
        if (fill < 0.001f)
        {
            return false;
        }

        if ((corner & 1) == 1)
        {
            invert = !invert;
        }

        if (!invert && fill > 0.999f)
        {
            return true;
        }

        float angle = Mathf.Clamp01(fill);

        if (invert)
        {
            angle = 1f - angle;
        }

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
            {
                xy[i3].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
            }
            else
            {
                xy[i1].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
            }
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
            {
                xy[i3].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
            }
            else
            {
                xy[i1].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
            }
        }
    }

    private static void AddQuad(VertexHelper vertexHelper, Vector3[] positions, Color32 quadColor, Vector3[] uvs)
    {
        int start = vertexHelper.currentVertCount;

        for (int i = 0; i < 4; i++)
        {
            vertexHelper.AddVert(positions[i], quadColor, uvs[i]);
        }

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

    private static int WordCountFor(int bitCount)
    {
        return (bitCount + 31) >> 5;
    }
}

#if UNITY_EDITOR
namespace UnityEditor.UI
{
    [CustomEditor(typeof(SegmentedImage), true)]
    [CanEditMultipleObjects]
    public class SegmentedImageEditor : GraphicEditor
    {
        private static readonly int[] SOriginValues2 =
        {
            0, 1
        };
        private static readonly int[] SOriginValues4 =
        {
            0, 1, 2, 3
        };
        private SerializedProperty _mFillMethod;
        private SerializedProperty _mFillOrigin;
        private SerializedProperty _mFillAmount;
        private SerializedProperty _mFillClockwise;
        private SerializedProperty _mType;
        private SerializedProperty _mFillCenter;
        private SerializedProperty _mSprite;
        private SerializedProperty _mPreserveAspect;
        private SerializedProperty _mUseSpriteMesh;
        private SerializedProperty _mPixelsPerUnitMultiplier;
        private SerializedProperty _mSegmentCount;
        private SerializedProperty _mSpacing;
        private SerializedProperty _mDirection;
        private SerializedProperty _mDrawMaskWords;
        private SerializedProperty _mValueMode;
        private SerializedProperty _mIntMaxValue;
        private SerializedProperty _mFloatMaxValue;
        private SerializedProperty _mEmptyColor;
        private SerializedProperty _mIsHandle;
        private SerializedProperty _mScript;
        private GUIContent _mSpriteContent;
        private GUIContent _mSpriteTypeContent;
        private GUIContent _mClockwiseContent;
        private AnimBool _mShowSlicedOrTiled;
        private AnimBool _mShowSliced;
        private AnimBool _mShowTiled;
        private AnimBool _mShowFilled;
        private bool _mIsDriven;

        protected override void OnEnable()
        {
            base.OnEnable();

            _mSpriteContent = EditorGUIUtility.TrTextContent("Source Image");
            _mSpriteTypeContent = EditorGUIUtility.TrTextContent("Image Type");
            _mClockwiseContent = EditorGUIUtility.TrTextContent("Clockwise");

            _mSprite = serializedObject.FindProperty("m_Sprite");
            _mType = serializedObject.FindProperty("m_Type");
            _mFillCenter = serializedObject.FindProperty("m_FillCenter");
            _mFillMethod = serializedObject.FindProperty("m_FillMethod");
            _mFillOrigin = serializedObject.FindProperty("m_FillOrigin");
            _mFillClockwise = serializedObject.FindProperty("m_FillClockwise");
            _mFillAmount = serializedObject.FindProperty("m_FillAmount");
            _mPreserveAspect = serializedObject.FindProperty("m_PreserveAspect");
            _mUseSpriteMesh = serializedObject.FindProperty("m_UseSpriteMesh");
            _mPixelsPerUnitMultiplier = serializedObject.FindProperty("m_PixelsPerUnitMultiplier");
            _mSegmentCount = serializedObject.FindProperty("_mSegmentCount");
            _mSpacing = serializedObject.FindProperty("_mSpacing");
            _mDirection = serializedObject.FindProperty("_mDirection");
            _mDrawMaskWords = serializedObject.FindProperty("_mDrawMaskWords");
            _mValueMode = serializedObject.FindProperty("_mValueMode");
            _mIntMaxValue = serializedObject.FindProperty("_mIntMaxValue");
            _mFloatMaxValue = serializedObject.FindProperty("_mFloatMaxValue");
            _mEmptyColor = serializedObject.FindProperty("_mEmptyColor");
            _mIsHandle = serializedObject.FindProperty("_mIsHandle");
            _mScript = serializedObject.FindProperty("m_Script");

            Image.Type typeEnum = (Image.Type)_mType.enumValueIndex;
            _mShowSlicedOrTiled = new AnimBool(!_mType.hasMultipleDifferentValues && (typeEnum == Image.Type.Sliced || typeEnum == Image.Type.Tiled));
            _mShowSliced = new AnimBool(!_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Sliced);
            _mShowTiled = new AnimBool(!_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Tiled);
            _mShowFilled = new AnimBool(!_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Filled);
            _mShowSlicedOrTiled.valueChanged.AddListener(Repaint);
            _mShowSliced.valueChanged.AddListener(Repaint);
            _mShowTiled.valueChanged.AddListener(Repaint);
            _mShowFilled.valueChanged.AddListener(Repaint);

            SetShowNativeSize(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _mShowSlicedOrTiled.valueChanged.RemoveListener(Repaint);
            _mShowSliced.valueChanged.RemoveListener(Repaint);
            _mShowTiled.valueChanged.RemoveListener(Repaint);
            _mShowFilled.valueChanged.RemoveListener(Repaint);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                if (_mScript != null)
                {
                    EditorGUILayout.PropertyField(_mScript);
                }
            }

            SegmentedImage image = (SegmentedImage)target;
            RectTransform rect = image.GetComponent<RectTransform>();
            _mIsDriven = (rect.drivenByObject as Slider)?.fillRect == rect;

            SpriteGUI();
            AppearanceControlsGUI();
            EditorGUILayout.PropertyField(_mEmptyColor, new GUIContent("Empty Color"));
            RaycastControlsGUI();
            MaskableControlsGUI();
            SegmentsGUI();

            TypeGUI();

            SetShowNativeSize(false);

            if (EditorGUILayout.BeginFadeGroup(m_ShowNativeSize.faded))
            {
                EditorGUI.indentLevel++;

                if ((Image.Type)_mType.enumValueIndex == Image.Type.Simple)
                {
                    EditorGUILayout.PropertyField(_mUseSpriteMesh);
                }

                EditorGUILayout.PropertyField(_mPreserveAspect);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFadeGroup();

            NativeSizeButtonGUI();

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (UnityEngine.Object obj in targets)
                {
                    if (obj is SegmentedImage segmentedImage)
                    {
                        segmentedImage.SetAllDirty();
                    }
                }
            }
        }

        private void SegmentsGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Segments", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_mDirection, new GUIContent("Direction"));
            EditorGUILayout.PropertyField(_mIsHandle, new GUIContent("Is Handle"));
            EditorGUILayout.PropertyField(_mSegmentCount, new GUIContent("Count"));
            EditorGUILayout.PropertyField(_mSpacing, new GUIContent("Spacing (px)"));
            DrawMaskGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel);
            DrawValueGUI();
        }

        private void SpriteGUI()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_mSprite, _mSpriteContent);

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Sprite newSprite = _mSprite.objectReferenceValue as Sprite;

            if (newSprite)
            {
                Image.Type oldType = (Image.Type)_mType.enumValueIndex;

                if (newSprite.border.sqrMagnitude > 0f)
                {
                    _mType.enumValueIndex = (int)Image.Type.Sliced;
                }
                else if (oldType == Image.Type.Sliced)
                {
                    _mType.enumValueIndex = (int)Image.Type.Simple;
                }
            }

            foreach (UnityEngine.Object obj in targets)
            {
                ((Image)obj).DisableSpriteOptimizations();
            }
        }

        private void TypeGUI()
        {
            EditorGUILayout.PropertyField(_mType, _mSpriteTypeContent);
            EditorGUI.indentLevel++;

            Image.Type typeEnum = (Image.Type)_mType.enumValueIndex;
            bool showSlicedOrTiled = !_mType.hasMultipleDifferentValues && (typeEnum == Image.Type.Sliced || typeEnum == Image.Type.Tiled);

            if (showSlicedOrTiled)
            {
                showSlicedOrTiled = AllSpritesHaveBorder();
            }

            _mShowSlicedOrTiled.target = showSlicedOrTiled;
            _mShowSliced.target = showSlicedOrTiled && !_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Sliced;
            _mShowTiled.target = showSlicedOrTiled && !_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Tiled;
            _mShowFilled.target = !_mType.hasMultipleDifferentValues && typeEnum == Image.Type.Filled;

            Sprite sprite = _mSprite.hasMultipleDifferentValues ? null : _mSprite.objectReferenceValue as Sprite;
            bool hasBorder = sprite != null && sprite.border.sqrMagnitude > 0f;

            if (EditorGUILayout.BeginFadeGroup(_mShowSlicedOrTiled.faded))
            {
                if (hasBorder)
                {
                    EditorGUILayout.PropertyField(_mFillCenter);
                }

                EditorGUILayout.PropertyField(_mPixelsPerUnitMultiplier);
            }

            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(_mShowSliced.faded))
            {
                if (sprite != null && !hasBorder)
                {
                    EditorGUILayout.HelpBox("This Image doesn't have a border.", MessageType.Warning);
                }
            }

            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(_mShowTiled.faded))
            {
                if (sprite != null && !hasBorder &&
                    ((sprite.texture != null && sprite.texture.wrapMode != TextureWrapMode.Repeat) || sprite.packed))
                {
                    EditorGUILayout
                        .HelpBox("It looks like you want to tile a sprite with no border. It would be more efficient to remove this Sprite from any SpriteAtlas and set the Wrap mode to Repeat.",
                                 MessageType.Warning);
                }
            }

            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(_mShowFilled.faded))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_mFillMethod);

                if (EditorGUI.EndChangeCheck())
                {
                    _mFillOrigin.intValue = 0;
                }

                Rect shapeRect = EditorGUILayout.GetControlRect(true);

                switch ((Image.FillMethod)_mFillMethod.enumValueIndex)
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

                if ((Image.FillMethod)_mFillMethod.enumValueIndex > Image.FillMethod.Vertical)
                {
                    EditorGUILayout.PropertyField(_mFillClockwise, _mClockwiseContent);
                }
            }

            EditorGUILayout.EndFadeGroup();

            EditorGUI.indentLevel--;
        }

        private bool AllSpritesHaveBorder()
        {
            foreach (UnityEngine.Object obj in targets)
            {
                SerializedObject targetSerializedObject = new(obj);
                SerializedProperty spriteProperty = targetSerializedObject.FindProperty("m_Sprite");
                Sprite sprite = spriteProperty.objectReferenceValue as Sprite;

                if (sprite == null || sprite.border.sqrMagnitude <= 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawFillOriginPopup(Rect rect, GUIContent[] options)
        {
            int[] values = options.Length == 2 ? SOriginValues2 : SOriginValues4;
            EditorGUI.IntPopup(rect, _mFillOrigin, options, values, Styles.FillOrigin);
        }

        private void DrawValueGUI()
        {
            if (_mIsDriven)
            {
                EditorGUILayout.HelpBox("The Fill Amount property is driven by Slider.", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(_mIsDriven))
            {
                EditorGUILayout.PropertyField(_mFillAmount, new GUIContent("Fill Amount"));
            }

            EditorGUILayout.PropertyField(_mValueMode, new GUIContent("Value Type"));

            if (_mValueMode.hasMultipleDifferentValues)
            {
                return;
            }

            if ((ValueMode)_mValueMode.enumValueIndex == ValueMode.Int)
            {
                DrawIntValueGUI();
            }
            else
            {
                DrawFloatValueGUI();
            }
        }

        private void DrawIntValueGUI()
        {
            int oldMax = Mathf.Max(1, _mIntMaxValue.intValue);
            int currentValue = Mathf.Clamp(Mathf.RoundToInt(_mFillAmount.floatValue * oldMax), 0, oldMax);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_mIntMaxValue, new GUIContent("Max Value"));

            if (EditorGUI.EndChangeCheck())
            {
                _mIntMaxValue.intValue = Mathf.Max(1, _mIntMaxValue.intValue);
                int newMax = _mIntMaxValue.intValue;
                _mFillAmount.floatValue = Mathf.Clamp(currentValue, 0, newMax) / (float)newMax;
            }

            int maxValue = Mathf.Max(1, _mIntMaxValue.intValue);
            DrawIntFillAmountSlider(_mFillAmount, maxValue, _mIsDriven);
        }

        private void DrawFloatValueGUI()
        {
            float oldMax = Mathf.Max(0.0001f, _mFloatMaxValue.floatValue);
            float currentValue = Mathf.Clamp(_mFillAmount.floatValue * oldMax, 0f, oldMax);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_mFloatMaxValue, new GUIContent("Max Value"));

            if (EditorGUI.EndChangeCheck())
            {
                _mFloatMaxValue.floatValue = Mathf.Max(0.0001f, _mFloatMaxValue.floatValue);
                float newMax = _mFloatMaxValue.floatValue;
                _mFillAmount.floatValue = Mathf.Clamp(currentValue, 0f, newMax) / newMax;
            }

            float maxValue = Mathf.Max(0.0001f, _mFloatMaxValue.floatValue);
            DrawFloatFillAmountSlider(_mFillAmount, maxValue, _mIsDriven);
        }

        private void DrawMaskGUI()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Draw Mask can be edited with one SegmentedImage selected.", MessageType.None);
                return;
            }

            int count = Mathf.Max(1, _mSegmentCount.intValue);
            const float BUTTON_WIDTH = 28f;
            float availableWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 42f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / BUTTON_WIDTH));

            EditorGUILayout.LabelField("Draw Mask");

            for (int rowStart = 0; rowStart < count; rowStart += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                int rowEnd = Mathf.Min(count, rowStart + perRow);

                for (int i = rowStart; i < rowEnd; i++)
                {
                    bool visible = GetMaskBit(i);
                    bool newVisible = GUILayout.Toggle(visible, i.ToString(), EditorStyles.miniButton, GUILayout.Width(BUTTON_WIDTH));

                    if (newVisible != visible)
                    {
                        SetMaskBit(i, newVisible);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("All"))
            {
                SetAllMaskBits(count, true);
            }

            if (GUILayout.Button("None"))
            {
                SetAllMaskBits(count, false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool GetMaskBit(int index)
        {
            int wordIndex = index >> 5;

            if (wordIndex >= _mDrawMaskWords.arraySize)
            {
                return true;
            }

            int word = _mDrawMaskWords.GetArrayElementAtIndex(wordIndex).intValue;
            return (word & (1 << (index & 31))) != 0;
        }

        private void SetMaskBit(int index, bool value)
        {
            int wordIndex = index >> 5;

            while (_mDrawMaskWords.arraySize <= wordIndex)
            {
                int newIndex = _mDrawMaskWords.arraySize;
                _mDrawMaskWords.arraySize = newIndex + 1;
                _mDrawMaskWords.GetArrayElementAtIndex(newIndex).intValue = -1;
            }

            SerializedProperty wordProperty = _mDrawMaskWords.GetArrayElementAtIndex(wordIndex);
            int mask = 1 << (index & 31);
            int word = wordProperty.intValue;
            wordProperty.intValue = value ? word | mask : word & ~mask;
        }

        private void SetAllMaskBits(int count, bool value)
        {
            int wordCount = (count + 31) >> 5;
            _mDrawMaskWords.arraySize = wordCount;

            for (int i = 0; i < wordCount; i++)
            {
                _mDrawMaskWords.GetArrayElementAtIndex(i).intValue = value ? -1 : 0;
            }
        }

        private void SetShowNativeSize(bool instant)
        {
            Image.Type type = (Image.Type)_mType.enumValueIndex;
            bool showNativeSize = (type == Image.Type.Simple || type == Image.Type.Filled) && _mSprite.objectReferenceValue != null;
            base.SetShowNativeSize(showNativeSize, instant);
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
                {
                    fillAmount.floatValue = newValue / (float)maxValue;
                }
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
                {
                    fillAmount.floatValue = newValue / maxValue;
                }
            }

            EditorGUI.showMixedValue = oldMixedValue;
            EditorGUI.EndProperty();
        }

        private static class Styles
        {
            public static readonly GUIContent FillOrigin = EditorGUIUtility.TrTextContent("Fill Origin");
            public static readonly GUIContent[] OriginHorizontal =
            {
                EditorGUIUtility.TrTextContent("Left"), EditorGUIUtility.TrTextContent("Right")
            };

            public static readonly GUIContent[] OriginVertical =
            {
                EditorGUIUtility.TrTextContent("Bottom"), EditorGUIUtility.TrTextContent("Top")
            };

            public static readonly GUIContent[] Origin90 =
            {
                EditorGUIUtility.TrTextContent("BottomLeft"), EditorGUIUtility.TrTextContent("TopLeft"), EditorGUIUtility.TrTextContent("TopRight"),
                EditorGUIUtility.TrTextContent("BottomRight")
            };

            public static readonly GUIContent[] Origin180 =
            {
                EditorGUIUtility.TrTextContent("Bottom"), EditorGUIUtility.TrTextContent("Left"), EditorGUIUtility.TrTextContent("Top"),
                EditorGUIUtility.TrTextContent("Right")
            };

            public static readonly GUIContent[] Origin360 =
            {
                EditorGUIUtility.TrTextContent("Bottom"), EditorGUIUtility.TrTextContent("Right"), EditorGUIUtility.TrTextContent("Top"),
                EditorGUIUtility.TrTextContent("Left")
            };
        }
    }
}
#endif
