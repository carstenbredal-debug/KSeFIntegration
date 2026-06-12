tableextension 50100 "KPHG Sales Header Ext" extends "Sales Header"
{
    fields
    {
        field(50100; "KPHG KSeF Required"; Boolean)
        {
            Caption = 'KSeF Required';
            DataClassification = CustomerContent;
        }
        field(50101; "KPHG KSeF Status"; Enum "KPHG KSeF Status")
        {
            Caption = 'KSeF Status';
            DataClassification = CustomerContent;
        }
        field(50102; "KPHG KSeF Error Message"; Text[250])
        {
            Caption = 'KSeF Error Message';
            DataClassification = CustomerContent;
        }
    }
}
