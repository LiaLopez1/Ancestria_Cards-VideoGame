Shader "UI/TurnGlow"
{
    // Shader de MARCO/BORDE brillante y pulsante para el highlighter de
    // turno. El centro del rectangulo queda transparente (se ve el panel
    // real detras); el marco se desvanece de ADENTRO hacia AFUERA - es
    // decir, se desvanece cerca del borde fisico del rectangulo, y tiene
    // un corte firme (no desvanecido) hacia el centro. No necesita un
    // sprite de halo - funciona con un sprite blanco liso o incluso sin
    // sprite asignado (usa la forma del RectTransform).
    //
    // Basado en el shader estandar UI/Default de Unity (mismo soporte de
    // stencil/mascara de Canvas), asi que sigue funcionando bien si el
    // highlighter queda dentro de una mascara. No usa nada especifico de
    // pipeline, asi que funciona igual en Built-in y en URP.
    //
    // Uso sugerido: RectTransform cuadrado (ancho == alto) para que el
    // grosor del borde se vea parejo en los 4 lados - si el rectangulo no
    // es cuadrado, el borde se ve mas grueso en el lado corto (limitacion
    // de calcular el borde en espacio UV 0-1).

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _GlowColor ("Color del brillo", Color) = (1, 0.85, 0.3, 1)
        _MinIntensity ("Intensidad minima", Range(0, 3)) = 0.6
        _MaxIntensity ("Intensidad maxima", Range(0, 5)) = 1.8
        _PulseSpeed ("Velocidad del pulso", Range(0.1, 10)) = 2.5

        _BorderThickness ("Ancho total del marco, desde el borde fisico hacia el centro (UV, 0-0.5)", Range(0.001, 0.5)) = 0.08
        _BorderSoftness ("Que tan lejos del borde fisico se desvanece antes de quedar solido (UV)", Range(0.001, 0.3)) = 0.03

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            fixed4 _GlowColor;
            float _MinIntensity;
            float _MaxIntensity;
            float _PulseSpeed;

            float _BorderThickness;
            float _BorderSoftness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);

                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);

                OUT.color = v.color * _Color;
                return OUT;
            }

            sampler2D _MainTex;

            fixed4 frag(v2f IN) : SV_Target
            {
                // Distancia (en espacio UV 0-1) al borde mas cercano del
                // rectangulo: 0 justo en el borde fisico, 0.5 en el centro.
                float distanciaAlBorde = min(min(IN.texcoord.x, 1.0 - IN.texcoord.x),
                                              min(IN.texcoord.y, 1.0 - IN.texcoord.y));

                // El desvanecido va de ADENTRO hacia AFUERA: aparece (fade
                // in) a medida que nos alejamos del borde fisico (distancia
                // 0) hasta llegar a opacidad completa en _BorderSoftness.
                float apareceDesdeElBorde = smoothstep(0.0, _BorderSoftness, distanciaAlBorde);

                // Corte firme hacia el centro: mas alla de _BorderThickness
                // ya no hay marco (transicion abrupta, no desvanecida).
                float limiteInterno = 1.0 - step(_BorderThickness, distanciaAlBorde);

                float mascaraDeMarco = apareceDesdeElBorde * limiteInterno;

                // Textura del sprite (opcional - un blanco liso funciona
                // perfecto), multiplicada por el tint y el color de brillo.
                half4 texColor = tex2D(_MainTex, IN.texcoord);
                half4 color = texColor * IN.color * _GlowColor;

                // Onda de pulso entre 0 y 1, remapeada a intensidad.
                float onda = (sin(_Time.y * _PulseSpeed) + 1.0) * 0.5;
                float intensidad = lerp(_MinIntensity, _MaxIntensity, onda);

                color.rgb *= intensidad;

                // El centro queda transparente: solo el marco tiene alpha,
                // asi se ve el panel real del jugador detras del centro.
                color.a *= mascaraDeMarco;

                #ifdef UNITY_UI_CLIP_RECT
                half alphaClip = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                color.a *= alphaClip;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
