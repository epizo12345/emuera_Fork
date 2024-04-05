using System;

namespace MinorShift.Emuera.Runtime;

enum InputType
{
    EnterKey = 1,//Enterキーかクリック
    AnyKey = 2,//なんでもいいから入力
    IntValue = 3,//整数値。OneInputかどうかは別の変数で
    StrValue = 4,//文字列。
    Void = 5,//入力不能。待つしかない→スキップ中orマクロ中ならなかったことになる

    //1823
    PrimitiveMouseKey = 11,

}


// 1819追加 入力・表示系とData、Process系の結合を弱くしよう計画の一つ
// できるだけ間にクッションをおいていきたい。最終的には別スレッドに

//クラスを毎回使い捨てるのはどうなんだろう 使いまわすべきか
internal sealed class InputRequest
{
    public InputRequest()
    {
        ID = LastRequestID++;
    }
    public readonly long ID;
    public InputType InputType;
    public bool NeedValue
    {
        get
        {
            return InputType == InputType.IntValue || InputType == InputType.StrValue
                || InputType == InputType.PrimitiveMouseKey;
        }
    }
    public bool OneInput;
    public bool StopMesskip;
    public bool IsSystemInput;

    public bool HasDefValue;
    public long DefIntValue;
    public string DefStrValue;

    public long Timelimit = -1;
    public bool DisplayTime;
    public string TimeUpMes;

    static long LastRequestID;
}
