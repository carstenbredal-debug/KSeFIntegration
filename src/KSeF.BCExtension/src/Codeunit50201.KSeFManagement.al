codeunit 50201 "KPHG KSeF Management"
{
    Permissions =
        tabledata "Sales Invoice Header" = RIMD,
        tabledata "KPHG KSeF Setup" = R;

    procedure MarkReady(var SalesInvHeader: Record "Sales Invoice Header")
    begin
        if not SalesInvHeader."KPHG KSeF Required" then
            Error('KSeF is not required for invoice %1.', SalesInvHeader."No.");

        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Ready;
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
    end;

    procedure SendToKSeF(var SalesInvHeader: Record "Sales Invoice Header")
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        Content: HttpContent;
        Headers: HttpHeaders;
        ResponseMessage: HttpResponseMessage;
        RequestBody: Text;
        ResponseText: Text;
        TextValue: Text;
        JsonResponse: JsonObject;
        JsonToken: JsonToken;
        Success: Boolean;
    begin
        if not SalesInvHeader."KPHG KSeF Required" then
            Error('KSeF is not required for invoice %1.', SalesInvHeader."No.");

        if SalesInvHeader."KPHG KSeF Status" = SalesInvHeader."KPHG KSeF Status"::Accepted then
            Error('Invoice %1 has already been accepted by KSeF.', SalesInvHeader."No.");

        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured. Please set up KSeF integration in the KSeF Setup page.');

        RequestBody := BuildInvoiceJson(SalesInvHeader);

        Content.WriteFrom(RequestBody);
        Content.GetHeaders(Headers);
        Headers.Remove('Content-Type');
        Headers.Add('Content-Type', 'application/json');

        Client.DefaultRequestHeaders().Add('x-functions-key', Setup."Azure Function Key");

        // Mark as Processing before the HTTP call
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Processing;
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
        Commit();

        Success := Client.Post(Setup."Azure Function URL" + '/invoice/submit', Content, ResponseMessage);

        if not Success then begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            SalesInvHeader."KPHG KSeF Error Message" := 'HTTP request failed.';
            SalesInvHeader.Modify(true);
            Error('Failed to connect to Azure Function.');
        end;

        ResponseMessage.Content().ReadAs(ResponseText);
        JsonResponse.ReadFrom(ResponseText);

        if not TryGetJsonText(JsonResponse, 'success', TextValue) then
            TextValue := '';

        if TextValue = 'true' then begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Sent;
            SalesInvHeader."KPHG KSeF Submission DT" := CurrentDateTime();
            SalesInvHeader."KPHG KSeF Error Message" := '';

            if TryGetJsonText(JsonResponse, 'elementReferenceNumber', TextValue) then
                SalesInvHeader."KPHG KSeF Element Ref." := CopyStr(TextValue, 1, 100);

            if TryGetJsonText(JsonResponse, 'sessionReferenceNumber', TextValue) then
                SalesInvHeader."KPHG KSeF Session Ref." := CopyStr(TextValue, 1, 100);

            if TryGetJsonText(JsonResponse, 'kSeFReferenceNumber', TextValue) then begin
                SalesInvHeader."KPHG KSeF Number" := CopyStr(TextValue, 1, 100);
                SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
                SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
            end;

            if TryGetJsonText(JsonResponse, 'qrVerificationUrl', TextValue) then
                SalesInvHeader."KPHG KSeF QR Reference" := CopyStr(TextValue, 1, 250);

            SalesInvHeader.Modify(true);
            Message('Invoice %1 submitted to KSeF successfully.', SalesInvHeader."No.");
        end else begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            if TryGetJsonText(JsonResponse, 'error', TextValue) then
                SalesInvHeader."KPHG KSeF Error Message" := CopyStr(FormatErrorMessage(TextValue), 1, 250)
            else
                SalesInvHeader."KPHG KSeF Error Message" := 'Unknown error from Azure Function.';
            SalesInvHeader.Modify(true);
            Error('KSeF submission failed: %1', SalesInvHeader."KPHG KSeF Error Message");
        end;
    end;

    procedure CheckStatus(var SalesInvHeader: Record "Sales Invoice Header")
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        ResponseMessage: HttpResponseMessage;
        ResponseText: Text;
        TextValue: Text;
        JsonResponse: JsonObject;
        JsonToken: JsonToken;
        Url: Text;
        Success: Boolean;
    begin
        if SalesInvHeader."KPHG KSeF Element Ref." = '' then
            Error('No KSeF Element Reference found for invoice %1. Submit the invoice first.', SalesInvHeader."No.");

        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured.');

        Url := Setup."Azure Function URL" + '/invoice/status/' + SalesInvHeader."KPHG KSeF Element Ref."
            + '?nip=' + Setup."Company NIP"
            + '&sessionRef=' + SalesInvHeader."KPHG KSeF Session Ref.";

        Client.DefaultRequestHeaders().Add('x-functions-key', Setup."Azure Function Key");
        Success := Client.Get(Url, ResponseMessage);

        if not Success then
            Error('Failed to connect to Azure Function.');

        ResponseMessage.Content().ReadAs(ResponseText);
        JsonResponse.ReadFrom(ResponseText);

        if not TryGetJsonText(JsonResponse, 'success', TextValue) then
            TextValue := '';

        if TextValue = 'true' then begin
            if TryGetJsonText(JsonResponse, 'kSeFReferenceNumber', TextValue) then begin
                SalesInvHeader."KPHG KSeF Number" := CopyStr(TextValue, 1, 100);
                SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
                SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
                SalesInvHeader."KPHG KSeF Error Message" := '';
                if TryGetJsonText(JsonResponse, 'qrVerificationUrl', TextValue) then
                    SalesInvHeader."KPHG KSeF QR Reference" := CopyStr(TextValue, 1, 250);
                SalesInvHeader.Modify(true);
                Message('Invoice %1 accepted by KSeF. Number: %2', SalesInvHeader."No.", SalesInvHeader."KPHG KSeF Number");
            end else begin
                if TryGetJsonText(JsonResponse, 'processingDescription', TextValue) then
                    Message('Invoice %1 still processing: %2', SalesInvHeader."No.", TextValue)
                else
                    Message('Invoice %1 still processing.', SalesInvHeader."No.");
            end;
        end else begin
            if TryGetJsonText(JsonResponse, 'error', TextValue) then
                SalesInvHeader."KPHG KSeF Error Message" := CopyStr(FormatErrorMessage(TextValue), 1, 250);
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            SalesInvHeader.Modify(true);
            Error('Status check failed: %1', SalesInvHeader."KPHG KSeF Error Message");
        end;
    end;

    procedure TestConnection()
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        ResponseMessage: HttpResponseMessage;
        ResponseText: Text;
        Success: Boolean;
    begin
        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured.');

        Success := Client.Get(Setup."Azure Function URL" + '/health', ResponseMessage);

        if Success and ResponseMessage.IsSuccessStatusCode() then
            Message('Connection successful! Azure Function is healthy.')
        else begin
            if ResponseMessage.Content().ReadAs(ResponseText) then
                Error('Connection failed: %1', ResponseText)
            else
                Error('Connection failed: Unable to reach Azure Function.');
        end;
    end;

    procedure MarkAccepted(var SalesInvHeader: Record "Sales Invoice Header"; KSeFNumber: Code[100])
    begin
        SalesInvHeader."KPHG KSeF Number" := KSeFNumber;
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
        SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
    end;

    procedure MarkRejected(var SalesInvHeader: Record "Sales Invoice Header"; ErrorMessage: Text[250])
    begin
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Rejected;
        SalesInvHeader."KPHG KSeF Error Message" := ErrorMessage;
        SalesInvHeader.Modify(true);
    end;

    local procedure FormatErrorMessage(RawError: Text): Text
    begin
        if RawError.Contains('ServiceUnavailable') or RawError.Contains('zamkni') then
            exit('KSeF service is temporarily unavailable. Please try again later.');
        if RawError.Contains('Unauthorized') or RawError.Contains('401') then
            exit('KSeF authentication failed. Please check the KSeF token in Azure Function settings.');
        if RawError.Contains('Forbidden') or RawError.Contains('403') then
            exit('Access denied by KSeF. Please verify your NIP and token permissions.');
        if RawError.Contains('BadRequest') or RawError.Contains('400') then
            exit('Invalid invoice data. Please check the invoice fields and try again.');
        if RawError.Contains('session init failed') then
            exit('Could not connect to KSeF. The service may be down for maintenance.');
        if RawError.Contains('invoice send failed') then
            exit('KSeF rejected the invoice. Please verify the invoice data is correct.');
        if StrLen(RawError) > 250 then
            exit(CopyStr(RawError, 1, 247) + '...');
        exit(RawError);
    end;

    local procedure BuildInvoiceJson(SalesInvHeader: Record "Sales Invoice Header"): Text
    var
        SalesInvLine: Record "Sales Invoice Line";
        CompanyInfo: Record "Company Information";
        Customer: Record Customer;
        Setup: Record "KPHG KSeF Setup";
        JsonObj: JsonObject;
        SellerObj: JsonObject;
        BuyerObj: JsonObject;
        LinesArray: JsonArray;
        LineObj: JsonObject;
        LineNo: Integer;
    begin
        CompanyInfo.Get();
        Customer.Get(SalesInvHeader."Sell-to Customer No.");
        Setup.GetSetup();

        JsonObj.Add('invoiceNumber', SalesInvHeader."No.");
        JsonObj.Add('issueDate', Format(SalesInvHeader."Posting Date", 0, '<Year4>-<Month,2>-<Day,2>'));
        JsonObj.Add('currencyCode', SalesInvHeader."Currency Code");

        if SalesInvHeader."Currency Code" = '' then
            JsonObj.Replace('currencyCode', 'PLN');

        // Seller
        SellerObj.Add('nip', Setup."Company NIP");
        SellerObj.Add('name', CompanyInfo.Name);
        SellerObj.Add('street', CompanyInfo.Address);
        SellerObj.Add('buildingNumber', '');
        SellerObj.Add('city', CompanyInfo.City);
        SellerObj.Add('postalCode', CompanyInfo."Post Code");
        SellerObj.Add('countryCode', 'PL');
        JsonObj.Add('seller', SellerObj);

        // Buyer
        BuyerObj.Add('nip', Customer."VAT Registration No.");
        BuyerObj.Add('name', SalesInvHeader."Sell-to Customer Name");
        BuyerObj.Add('street', SalesInvHeader."Sell-to Address");
        BuyerObj.Add('buildingNumber', '');
        BuyerObj.Add('city', SalesInvHeader."Sell-to City");
        BuyerObj.Add('postalCode', SalesInvHeader."Sell-to Post Code");
        BuyerObj.Add('countryCode', SalesInvHeader."Sell-to Country/Region Code");
        JsonObj.Add('buyer', BuyerObj);

        // Payment
        JsonObj.Add('paymentMethod', 'transfer');
        if SalesInvHeader."Due Date" <> 0D then
            JsonObj.Add('paymentDueDate', Format(SalesInvHeader."Due Date", 0, '<Year4>-<Month,2>-<Day,2>'));

        // Lines
        LineNo := 0;
        SalesInvLine.SetRange("Document No.", SalesInvHeader."No.");
        SalesInvLine.SetFilter(Type, '<>%1', SalesInvLine.Type::" ");
        if SalesInvLine.FindSet() then
            repeat
                LineNo += 1;
                Clear(LineObj);
                LineObj.Add('lineNumber', LineNo);
                LineObj.Add('description', SalesInvLine.Description);
                LineObj.Add('quantity', SalesInvLine.Quantity);
                LineObj.Add('unitOfMeasure', SalesInvLine."Unit of Measure Code");
                LineObj.Add('unitPrice', SalesInvLine."Unit Price");
                LineObj.Add('netAmount', SalesInvLine."Line Amount");
                LineObj.Add('vatRate', SalesInvLine."VAT %");
                LineObj.Add('vatAmount', SalesInvLine."Amount Including VAT" - SalesInvLine.Amount);
                LineObj.Add('grossAmount', SalesInvLine."Amount Including VAT");
                LinesArray.Add(LineObj);
            until SalesInvLine.Next() = 0;

        JsonObj.Add('lines', LinesArray);

        exit(Format(JsonObj));
    end;

    [TryFunction]
    local procedure TryGetJsonText(JsonObj: JsonObject; PropertyName: Text; var Result: Text)
    var
        JsonToken: JsonToken;
    begin
        JsonObj.Get(PropertyName, JsonToken);
        Result := JsonToken.AsValue().AsText();
        if Result = '' then
            Error('');
    end;
}
