tableextension 50105 "KPHG Sales Cr Memo Line Ext" extends "Sales Cr.Memo Line"
{
    fields
    {
        field(50120; "KPHG Lot No."; Code[50])
        {
            Caption = 'Lot No.';
            DataClassification = CustomerContent;
        }
        field(50121; "KPHG Line Component"; Enum "KPHG Line Component")
        {
            Caption = 'Line Component';
            DataClassification = CustomerContent;
        }
        field(50122; "KPHG Settlement Reference"; Code[50])
        {
            Caption = 'Settlement Reference';
            DataClassification = CustomerContent;
        }
        field(50123; "KPHG GTU Code"; Enum "KPHG GTU Code")
        {
            Caption = 'GTU Code';
            DataClassification = CustomerContent;
        }
        field(50124; "KPHG PKWiU"; Code[20])
        {
            Caption = 'PKWiU';
            DataClassification = CustomerContent;
        }
    }
}
