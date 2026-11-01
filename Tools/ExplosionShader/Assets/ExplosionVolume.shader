Shader "ChitakTech/ExplosionVolume"
{
    SubShader
    {
        CGINCLUDE
            #include "UnityCG.cginc"
            float4 _CenterRadius;
            float4 _Effect; // animation seconds, random seed, opacity, shockwave mode
            float hash(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash(cell), hash(cell + float3(1,0,0)), f.x),
                    lerp(hash(cell + float3(0,1,0)), hash(cell + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash(cell + float3(0,0,1)), hash(cell + float3(1,0,1)), f.x),
                    lerp(hash(cell + float3(0,1,1)), hash(cell + float3(1,1,1)), f.x), f.y), f.z);
            }
            float turbulence(float3 p)
            {
                float value = 0, weight = .55;
                for (int octave = 0; octave < 4; octave++)
                {
                    value += weight * noise(p);
                    p = p * 2.07 + float3(5.2, 1.3, 7.1);
                    weight *= .5;
                }
                return value;
            }
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
                // The shockwave is a translucent shell: when inside, see its far wall,
                // not a full-screen translucent fill attached to the near plane.
                if (_Effect.w > .5 && enter < nearRay) distance = leave;
                float eyeDepth = originDepth + distance * facing;
                // The scaled-space camera handles distant effects; do not pull an
                // out-of-range surface in front of nearer local scenery.
                clip(_Clip.y * .99999 - eyeDepth);
                float4 projected = mul(_Projection, float4(0, 0, -eyeDepth, 1));
                output o;
                o.depth = projected.z / projected.w;
                if (UNITY_NEAR_CLIP_VALUE < 0) o.depth = o.depth * .5 + .5;
                float3 samplePoint = ray * (distance / _CenterRadius.w) - center;
                float3 flow = float3(.18, -.3, .12) * _Effect.x + _Effect.y;
                // Sample inside the volume as well as its surface so the view remains
                // turbulent when the camera is within a very large fireball.
                float clouds = turbulence(samplePoint * 5 + flow);
                float inner = turbulence((samplePoint + ray * .45) * 9 - flow * .7);
                float heat = saturate((clouds * .7 + inner * .3 - .15) * 1.8);
                float3 color = lerp(float3(.32, .012, .001), float3(1.6, 1.05, .25), heat);
                float alpha = 1;
                if (_Effect.w > .5)
                {
                    color = float3(1.4, 1.15, .85) * (.85 + clouds * .15);
                    alpha = _Effect.z * (.25 + .75 * pow(1 - saturate(halfChord), 2));
                }
                // Unity's fog equations, with explicit per-camera settings. Bundles
                // must not depend on fog variants being retained by KSP's build.
                float visibility = 1;
                if (_Fog.x == 1) visibility = saturate((_Fog.z - eyeDepth) / max(.0001, _Fog.z - _Fog.y));
                if (_Fog.x == 2) visibility = exp(-_Fog.w * eyeDepth);
                if (_Fog.x == 3) visibility = exp(-pow(_Fog.w * eyeDepth, 2));
                color = lerp(_FogColor.rgb, color, visibility);
                o.color = float4(color, alpha);
                return o;
            }
        ENDCG
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }
    }
}
