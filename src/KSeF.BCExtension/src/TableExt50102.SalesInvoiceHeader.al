tableextension 50102 "KPHG Sales Inv Header Ext" extends "Sales Invoice Header"
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
        field(50103; "KPHG KSeF Number"; Code[100])
        {
            Caption = 'KSeF Number';
            DataClassification = CustomerContent;
        }
        field(50104; "KPHG KSeF Submission DT"; DateTime)
        {
            Caption = 'KSeF Submission DateTime';
            DataClassification = CustomerContent;
        }
        field(50105; "KPHG KSeF Acceptance DT"; DateTime)
        {
            Caption = 'KSeF Acceptance DateTime';
            DataClassification = CustomerContent;
        }
        field(50106; "KPHG KSeF QR Reference"; Text[250])
        {
            Caption = 'KSeF QR Reference';
            DataClassification = CustomerContent;
        }
        field(50107; "KPHG KSeF Payload URL"; Text[250])
        {
            Caption = 'KSeF Payload URL';
            DataClassification = CustomerContent;
        }
        field(50108; "KPHG KSeF Response URL"; Text[250])
        {
            Caption = 'KSeF Response URL';
            DataClassification = CustomerContent;
        }
        field(50109; "KPHG KSeF Element Ref."; Code[100])
        {
            Caption = 'KSeF Element Reference No.';
            DataClassification = CustomerContent;
        }
    }
}
