Shader "ChitakTech/ExplosionVolume"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _CenterRadius;
            float3 _Ray00, _Ray10, _Ray01, _Ray11;
            float3 _Origin00, _Origin10, _Origin01, _Origin11;
            float3 _Forward;
            float4 _Clip; // near, far, orthographic
            float4 _Fog; // mode (0 off, 1 linear, 2 exp, 3 exp2), start, end, density
            float4 _FogColor;
            float4x4 _Projection;
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.vertex = float4(vertex.xy, 0, 1);
                o.uv = vertex.xy * .5 + .5;
                return o;
            }
            struct output { float4 color : SV_Target; float depth : SV_Depth; };
            output frag(v2f i)
            {
                float3 ray = normalize(lerp(lerp(_Ray00, _Ray10, i.uv.x), lerp(_Ray01, _Ray11, i.uv.x), i.uv.y));
                float3 origin = lerp(lerp(_Origin00, _Origin10, i.uv.x), lerp(_Origin01, _Origin11, i.uv.x), i.uv.y);
                // Work in radius units so a 100,000-km blast is as stable as a small one.
                float3 center = _CenterRadius.xyz - origin / _CenterRadius.w;
                float along = dot(center, ray);
                float3 perpendicular = center - ray * along;
                float discriminant = 1 - dot(perpendicular, perpendicular);
                clip(discriminant);
                float halfChord = sqrt(max(0, discriminant));
                float enter = (along - halfChord) * _CenterRadius.w;
                float leave = (along + halfChord) * _CenterRadius.w;
                float facing = max(.000001, dot(ray, _Forward));
                float originDepth = dot(origin, _Forward);
                float nearRay = max(0, (_Clip.x * 1.001 - originDepth) / facing);
                clip(leave - nearRay);
                float distance = max(enter, nearRay);
                float eyeDepth = originDepth + distance * facing;
                // The scaled-space camera handles distant effects; do not pull an
                // out-of-range surface in front of nearer local scenery.
                clip(_Clip.y * .99999 - eyeDepth);
                float4 projected = mul(_Projection, float4(0, 0, -eyeDepth, 1));
                output o;
                o.depth = projected.z / projected.w;
                if (UNITY_NEAR_CLIP_VALUE < 0) o.depth = o.depth * .5 + .5;
                float heat = saturate(halfChord);
                float3 color = lerp(float3(1, .12, .005), float3(1, .75, .12), heat);
                // Unity's fog equations, with explicit per-camera settings. Bundles
                // must not depend on fog variants being retained by KSP's build.
                float visibility = 1;
                if (_Fog.x == 1) visibility = saturate((_Fog.z - eyeDepth) / max(.0001, _Fog.z - _Fog.y));
                if (_Fog.x == 2) visibility = exp(-_Fog.w * eyeDepth);
                if (_Fog.x == 3) visibility = exp(-pow(_Fog.w * eyeDepth, 2));
                color = lerp(_FogColor.rgb, color, visibility);
                o.color = float4(color, 1);
                return o;
            }
            ENDCG
        }
    }
}
