// Разделимое гауссово размытие для снимка под меню паузы (UiBackdropBlur).
// Один проход по направлению _Direction (шаг в UV); вызывается по горизонтали и вертикали.
Shader "Hidden/Razlom/UiBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Direction;

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 d = _Direction.xy;
                fixed4 c = tex2D(_MainTex, i.uv) * 0.227027;
                c += (tex2D(_MainTex, i.uv + d * 1.384615) + tex2D(_MainTex, i.uv - d * 1.384615)) * 0.316216;
                c += (tex2D(_MainTex, i.uv + d * 3.230769) + tex2D(_MainTex, i.uv - d * 3.230769)) * 0.070270;
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
