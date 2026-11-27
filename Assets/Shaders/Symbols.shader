// Component bodies from the symbol atlas, drawn for all components in one procedural instanced draw by
// LogicSim.Views.SymbolRenderer. The atlas channels are signed distance fields: R outline, G fill, B glow.
// See docs/SYMBOL_FORMAT.md.
Shader "LogicSim/Symbols"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            // Keep in sync with SymbolRenderer.InstanceData and SymbolRenderer.SymbolData.
            struct SymbolInstance
            {
                float2 position;    // world position of the component's origin (its top-left corner)
                uint symbol;        // index into _Symbols
                uint flags;         // bits 0-1 quarter turns, bit 2 selected, bit 3 lit by its own state, bits 8-15 input pin rows, bits 16-23 output pin rows
            };

            struct SymbolData
            {
                float4 uv;          // atlas rect including the padding: x, y, width, height
                float4 size;        // xy: size in units
                float4 left0;       // rows 0-3: where the outline starts, units from the left edge, -1 if none
                float4 left1;       // rows 4-7
                float4 right0;      // rows 0-3: where the outline ends
                float4 right1;      // rows 4-7
            };

            StructuredBuffer<SymbolInstance> _Instances;
            StructuredBuffer<SymbolData> _Symbols;
            // Per instance 17 nets (-1 for none): input rows 0-7, output rows 0-7, then the net that lights the fill.
            StructuredBuffer<int> _InstanceNets;
            // Net values, one byte per net, four nets per word (see NetValues).
            StructuredBuffer<uint> _Nets;
            float _ShowValues;

            sampler2D _MainTex;
            float _Padding;         // margin around each symbol in its atlas rect, units
            float _Spread;          // distance range of the fields on each side of an edge, units
            float _StrokeHalfWidth; // half the outline stroke, units
            float _StubHalfWidth;   // half the width of a pin stub, units (wires are as wide)
            float _SelectedGrow;    // extra outline width of a selected component, units
            float _LodStart;        // units per pixel where the solid rectangle starts to fade in
            float _LodEnd;          // units per pixel from where only the rectangle is drawn
            float4 _OutlineColor;
            float4 _FillOnColor;
            float4 _FillOffColor;
            float4 _GlowColor;
            float4 _SelectionColor;
            float4 _WireColor;
            float4 _HighColor;
            float4 _LowColor;
            float4 _LodOffColor;

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 local : TEXCOORD0;   // component space, units: x right, y up, origin top-left
                float2 uv : TEXCOORD1;
                nointerpolation uint instance : TEXCOORD2;
                // Bit 0 lit, bit 1 values shown, bits 8-15 input values by row, bits 16-23 output values by row.
                nointerpolation uint state : TEXCOORD3;
            };

            uint NetValue(int net)
            {
                if (net < 0)
                    return 0;
                uint word = _Nets[(uint)net >> 2];
                return ((word >> (((uint)net & 3) * 8)) & 0xFF) != 0 ? 1 : 0;
            }

            static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(1, 0), float2(1, 1), float2(0, 1) };

            // Clockwise quarter turns, like the views' transform.rotation = Euler(0, 0, -90 * turns).
            float2 Rotate(float2 p, uint turns)
            {
                turns &= 3;
                if (turns == 1) return float2(p.y, -p.x);
                if (turns == 2) return -p;
                if (turns == 3) return float2(-p.y, p.x);
                return p;
            }

            v2f vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                SymbolInstance instance = _Instances[instanceId];
                SymbolData symbol = _Symbols[instance.symbol];
                float2 corner = Corners[vertexId % 6];
                float2 size = symbol.size.xy;
                float2 local = float2(lerp(-_Padding, size.x + _Padding, corner.x), lerp(-size.y - _Padding, _Padding, corner.y));
                float2 world = instance.position + Rotate(local, instance.flags);

                v2f o;
                o.position = mul(UNITY_MATRIX_VP, float4(world, 0, 1));
                o.local = local;
                o.uv = symbol.uv.xy + corner * symbol.uv.zw;
                o.instance = instanceId;

                uint state = (instance.flags & 8) != 0 ? 1 : 0;
                if (_ShowValues > 0.5)
                {
                    uint nets = instanceId * 17;
                    state |= 2 | NetValue(_InstanceNets[nets + 16]);
                    for (uint row = 0; row < 8; row++)
                    {
                        state |= NetValue(_InstanceNets[nets + row]) << (8 + row);
                        state |= NetValue(_InstanceNets[nets + 8 + row]) << (16 + row);
                    }
                }
                o.state = state;
                return o;
            }

            float Coverage(float distance, float pixel)
            {
                return saturate(distance / pixel + 0.5);
            }

            // Premultiplied "over": paints color with coverage alpha on top of below.
            float4 Over(float4 below, float3 color, float alpha)
            {
                return float4(color * alpha + below.rgb * (1 - alpha), alpha + below.a * (1 - alpha));
            }

            float RowValue(float4 rows0, float4 rows1, int row)
            {
                return row < 4 ? rows0[row] : rows1[row - 4];
            }

            float4 frag(v2f i) : SV_Target
            {
                SymbolInstance instance = _Instances[i.instance];
                SymbolData symbol = _Symbols[instance.symbol];
                uint state = i.state;
                float2 local = i.local;
                float2 size = symbol.size.xy;

                // Size of a screen pixel in units, the same in every direction for an orthographic camera.
                float pixel = max(max(length(ddx(local)), length(ddy(local))), 1e-5);
                float3 distance = (tex2D(_MainTex, i.uv).rgb - 0.5) * (2 * _Spread);

                bool lit = (state & 1) != 0;
                bool values = (state & 2) != 0;
                bool selected = (instance.flags & 4) != 0;
                float4 color = 0;

                if (lit)
                    color = Over(color, _GlowColor.rgb, _GlowColor.a * Coverage(distance.b, pixel));

                // Pin stubs, under the body: from an input pin at (0, -row) to the outline, and from the outline to an
                // output pin at (width, -row). They join the wires and are colored like them.
                int row = (int)round(-local.y);
                if (row >= 0 && row < 8)
                {
                    uint bit = 1u << row;
                    float across = Coverage(max(_StubHalfWidth, 0.5 * pixel) - abs(local.y + row), pixel);
                    if (((instance.flags >> 8) & bit) != 0)
                    {
                        float edge = RowValue(symbol.left0, symbol.left1, row);
                        float end = edge >= 0 ? edge + _StrokeHalfWidth : 0.5 * size.x;
                        float along = min(Coverage(local.x, pixel), Coverage(end - local.x, pixel));
                        float3 stub = values ? (((state >> 8) & bit) != 0 ? _HighColor.rgb : _LowColor.rgb) : _WireColor.rgb;
                        color = Over(color, stub, across * along);
                    }
                    if (((instance.flags >> 16) & bit) != 0)
                    {
                        float edge = RowValue(symbol.right0, symbol.right1, row);
                        float start = edge >= 0 ? edge - _StrokeHalfWidth : 0.5 * size.x;
                        float along = min(Coverage(local.x - start, pixel), Coverage(size.x - local.x, pixel));
                        float3 stub = values ? (((state >> 16) & bit) != 0 ? _HighColor.rgb : _LowColor.rgb) : _WireColor.rgb;
                        color = Over(color, stub, across * along);
                    }
                }

                color = Over(color, lit ? _FillOnColor.rgb : _FillOffColor.rgb, Coverage(distance.g, pixel));

                // Strokes stay at least a pixel wide when zoomed out.
                float grow = max(0, 0.5 * pixel - _StrokeHalfWidth) + (selected ? _SelectedGrow : 0);
                color = Over(color, selected ? _SelectionColor.rgb : _OutlineColor.rgb, Coverage(distance.r + grow, pixel));

                // Far zoomed out the symbols turn into solid rectangles of the footprint in the state color.
                float lod = smoothstep(_LodStart, _LodEnd, pixel);
                if (lod > 0)
                {
                    float inside = Coverage(min(min(local.x, size.x - local.x), min(-local.y, size.y + local.y)), pixel);
                    float3 solid = selected ? _SelectionColor.rgb : lit ? _FillOnColor.rgb : _LodOffColor.rgb;
                    color = lerp(color, float4(solid * inside, inside), lod);
                }
                return color;
            }
            ENDHLSL
        }
    }
}
