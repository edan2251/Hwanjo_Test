Shader "BossFight/DocheolWhiteFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _WhiteFlash ("White Flash", Range(0,1)) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"
            float _WhiteFlash;
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 color=SampleSpriteTexture(i.texcoord)*i.color;
                color.rgb=lerp(color.rgb,fixed3(1,1,1),saturate(_WhiteFlash));
                color.rgb*=color.a;
                return color;
            }
            ENDCG
        }
    }
}
