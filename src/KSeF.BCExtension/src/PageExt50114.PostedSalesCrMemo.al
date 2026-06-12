pageextension 50114 "KPHG Posted Sales CrMemo Ext" extends "Posted Sales Credit Memo"
{
    layout
    {
        addlast(General)
        {
            group("KPHG KSeF")
            {
                Caption = 'KSeF';
                field("KPHG KSeF Required"; Rec."KPHG KSeF Required") { ApplicationArea = All; }
                field("KPHG KSeF Status"; Rec."KPHG KSeF Status") { ApplicationArea = All; }
                field("KPHG KSeF Number"; Rec."KPHG KSeF Number") { ApplicationArea = All; }
                field("KPHG KSeF Element Ref."; Rec."KPHG KSeF Element Ref.") { ApplicationArea = All; }
                field("KPHG KSeF Submission DT"; Rec."KPHG KSeF Submission DT") { ApplicationArea = All; }
                field("KPHG KSeF Acceptance DT"; Rec."KPHG KSeF Acceptance DT") { ApplicationArea = All; }
                field("KPHG Original Invoice KSeF No."; Rec."KPHG Original Invoice KSeF No.") { ApplicationArea = All; }
                field("KPHG KSeF Error Message"; Rec."KPHG KSeF Error Message") { ApplicationArea = All; }
            }
        }
    }

    actions
    {
        addlast(Processing)
        {
            action("KPHG Send to KSeF")
            {
                ApplicationArea = All;
                Caption = 'Send to KSeF';
                Image = ElectronicDocument;

                trigger OnAction()
                begin
                    Message('Credit memo KSeF submission will be implemented in v2.');
                end;
            }
        }
    }
}
