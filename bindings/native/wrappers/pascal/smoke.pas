{
  The Pascal side of the boundary, exercised rather than described.

  Takes the same four arguments as the C sample, and writes the PDF out, so the two
  can be compared byte for byte. That comparison is the point: this program passed
  every check it made while quietly corrupting the accented data on its way in, and
  only the size of the resulting PDF gave it away.
}
program smoke;

{$IFDEF FPC}
  {$MODE OBJFPC}{$H+}
  { This file is UTF-8, and the compiler has to be told so. Without it a literal
    with an accent in it is read in the system code page, and comparing one against
    data that really is UTF-8 fails for a reason that has nothing to do with the
    library - which is how the first run of this program pointed at the wrong thing. }
  {$CODEPAGE UTF8}
{$ENDIF}

uses
  SysUtils, Classes, Frisket;

var
  Failed: Integer = 0;

procedure Check(const What: string; Ok: Boolean);
begin
  if Ok then
    WriteLn('  ok   ', What)
  else
  begin
    WriteLn('  FAIL ', What);
    Inc(Failed);
  end;
end;

{ True when the text holds a byte no ASCII string could. }
function HasHighByte(const Text: UTF8String): Boolean;
var
  I: Integer;
begin
  for I := 1 to Length(Text) do
    if Ord(Text[I]) >= $80 then
      Exit(True);
  Result := False;
end;

{ A file as bytes. A font is not text and holds zeroes, so it cannot travel in a
  string here the way the JSON does. }
function ReadAllBytes(const FileName: string): TBytes;
var
  Stream: TFileStream;
begin
  Stream := TFileStream.Create(FileName, fmOpenRead or fmShareDenyNone);
  try
    SetLength(Result, Stream.Size);
    if Stream.Size > 0 then
      Stream.ReadBuffer(Result[0], Stream.Size);
  finally
    Stream.Free;
  end;
end;

var
  Fonts: TErFonts;
  Picked: TErFonts;
  FontBytes: TBytes;
  Nonsense: UTF8String;
  Template: TErTemplate;
  Options: TErOptions;
  Data, Html, Svg, Issues: UTF8String;
  Pdf: TBytes;
  Faces: Integer;
  Refused: Boolean;
begin
  if ParamCount < 4 then
  begin
    WriteLn('usage: smoke <font-dir> <template.json> <data.json> <out.pdf> [font-file]');
    Halt(2);
  end;

  WriteLn('frisket ', ErVersion);

  Fonts := TErFonts.Create;
  Template := nil;
  try
    Faces := Fonts.AddDirectory(ParamStr(1));
    Check('fonts are found', Faces > 0);
    WriteLn('       ', Faces, ' faces');

    { A font the caller chose. Unlike AddDirectory, a failure is raised: a scan was
      never asked about any file in particular, and a font somebody named is one
      somebody wants, so skipping it in silence leaves them looking at a list it is
      not in with nothing to say why. }
    if ParamCount >= 5 then
    begin
      Picked := TErFonts.Create;
      try
        Picked.AddFile(ParamStr(5));
        Check('one font file, named on purpose', Copy(Picked.ListAsJson, 1, 1) = '[');

        FontBytes := ReadAllBytes(ParamStr(5));
        Check('its bytes were read', Length(FontBytes) > 0);
        Picked.AddMemory(FontBytes[0], Length(FontBytes));

        Refused := False;
        try
          Nonsense := 'not a font';
          Picked.AddMemory(Nonsense[1], Length(Nonsense));
        except
          on E: Exception do Refused := True;
        end;
        Check('what is not a font is refused, not stepped over', Refused);
      finally
        Picked.Free;
      end;
    end;

    Fonts.SetDefaultFamily('Mulish');
    Check('the library lists itself', Copy(Fonts.ListAsJson, 1, 1) = '[');

    Template := TErTemplate.CreateFromFile(ParamStr(2));
    Check('the template loads', True);

    { Reported, not required: what the validator finds belongs to the template that
      was passed in, and a sample must work with whichever one that is. }
    Issues := Template.CheckAsJson(True);
    Check('the validator answers', (Length(Issues) > 0) and (Issues[1] = '['));
    WriteLn('       check -> ', Issues);

    { Read as the bytes it holds. A TStringList here would decode the file into the
      system code page and hand back something else, which is exactly the bug this
      program was written to miss. }
    Data := ErReadFileUtf8(ParamStr(3));
    Check('the data file is readable', Length(Data) > 100);

    { The crossing that matters, and the one that broke here once: a wrapper that
      decodes the file into the system code page loses every character outside it,
      and nothing fails. The document comes out slightly wrong, and only the bytes
      give it away. }
    Check('and non-ASCII data survives being read', HasHighByte(Data));

    Options := TErOptions.Default;
    Pdf := Template.RenderToBytes(Fonts, Data, Options);
    Check('a PDF comes out', Length(Pdf) > 1000);
    Check('and it is a PDF', (Length(Pdf) > 5) and (Chr(Pdf[0]) = '%') and (Chr(Pdf[1]) = 'P'));

    Template.RenderToFile(Fonts, Data, ParamStr(4), Options);
    WriteLn('       ', Length(Pdf), ' bytes -> ', ParamStr(4));

    Options.Format := erHtml;
    Html := Template.RenderToText(Fonts, Data, Options);
    Check('HTML comes out', Pos(UTF8String('<svg'), Html) > 0);

    Options.Format := erSvg;
    Svg := Template.RenderToText(Fonts, Data, Options);
    Check('SVG comes out as an envelope', (Pos(UTF8String('"pages":['), Svg) > 0)
                                          and (Pos(UTF8String('"pageWidth":210'), Svg) > 0));

    { A refusal has to arrive as an exception carrying the reason, or a caller has no
      way to tell a bad option from a blank page. }
    Refused := False;
    try
      Options.Format := erPdf;
      Template.RenderToText(nil, Data, Options);
    except
      on E: EFrisket do
      begin
        Refused := True;
        WriteLn('       refused -> ', E.Message);
      end;
    end;
    Check('a missing font library is refused', Refused);

  finally
    Template.Free;
    Fonts.Free;
  end;

  if Failed > 0 then
  begin
    WriteLn;
    WriteLn(Failed, ' FAILED');
    Halt(1);
  end;

  WriteLn;
  WriteLn('all good');
end.
