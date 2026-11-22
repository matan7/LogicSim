// All wires in one procedural instanced draw by LogicSim.Views.WireRenderer: one quad per straight segment, colored
// from the simulation's net values on the GPU. Loaded from Resources (Shaders/Wires) so that builds include it.
Shader "LogicSim/Wires"
{
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

            // Keep in sync with WireRenderer.SegmentData and WireRenderer.WireData.
            struct Segment
            {
                float2 a;
                float2 b;
                uint wire;      // index into _Wires; 0xFFFFFFFF for an unused segment
                uint pad;
            };

            struct WireData
            {
                int net;        // index into the net values, -1 without one
                uint flags;     // bit 0 selected
            };

            StructuredBuffer<Segment> _Segments;
            StructuredBuffer<WireData> _Wires;
            // Net values, one byte per net, four nets per word.
            StructuredBuffer<uint> _Nets;

            float _HalfWidth;
            float _SelectedHalfWidth;
            float _ShowValues;      // 1 while the simulation shows values
            float4 _WireColor;
            float4 _HighColor;
            float4 _LowColor;
            float4 _SelectionColor;

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 coord : TEXCOORD0;               // along the segment (from a), across it (from the center line)
                nointerpolation float3 shape : TEXCOORD1;   // length, half width, pixel size
                nointerpolation float4 color : COLOR;
            };

            static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(1, 0), float2(1, 1), float2(0, 1) };

            v2f vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                v2f o;
                Segment segment = _Segments[instanceId];
                if (segment.wire == 0xFFFFFFFF)
                {
                    o.position = float4(0, 0, 0, 0);    // degenerate: nothing is drawn
                    o.coord = 0;
                    o.shape = 0;
                    o.color = 0;
                    return o;
                }
                WireData wire = _Wires[segment.wire];
                bool selected = (wire.flags & 1) != 0;

                // Units per pixel of the orthographic camera; wires stay at least one pixel wide.
                float pixel = 2 * unity_OrthoParams.y / _ScreenParams.y;
                float halfWidth = max(selected ? _SelectedHalfWidth : _HalfWidth, 0.5 * pixel);

                float2 delta = segment.b - segment.a;
                float len = length(delta);
                float2 dir = len > 1e-5 ? delta / len : float2(1, 0);
                float2 normal = float2(-dir.y, dir.x);
                // Square caps of half the width join the segments at the corners; one more pixel for the antialiasing.
                float reach = halfWidth + pixel;
                float2 corner = Corners[vertexId % 6];
                float along = lerp(-reach, len + reach, corner.x);
                float across = lerp(-reach, reach, corner.y);
                float2 world = segment.a + dir * along + normal * across;
                o.position = mul(UNITY_MATRIX_VP, float4(world, 0, 1));
                o.coord = float2(along, across);
                o.shape = float3(len, halfWidth, pixel);

                float4 color = _WireColor;
                if (_ShowValues > 0.5 && wire.net >= 0)
                {
                    uint word = _Nets[(uint)wire.net >> 2];
                    uint value = (word >> (((uint)wire.net & 3) * 8)) & 0xFF;
                    color = value != 0 ? _HighColor : _LowColor;
                }
                o.color = selected ? _SelectionColor : color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float len = i.shape.x, halfWidth = i.shape.y, pixel = max(i.shape.z, 1e-5);
                float side = halfWidth - abs(i.coord.y);
                float ends = min(i.coord.x + halfWidth, len + halfWidth - i.coord.x);
                float coverage = saturate(min(side, ends) / pixel + 0.5);
                return float4(i.color.rgb * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
