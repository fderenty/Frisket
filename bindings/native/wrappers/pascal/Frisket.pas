{
  Frisket for Object Pascal - Free Pascal and Delphi.

  Binds the flat C interface late, by name, so there is nothing to link and the
  library can sit beside the executable or anywhere on the search path.

  ------------------------------------------------------------------------------
  On strings, which is where this unit was wrong once and is worth reading twice.

  Everything that crosses the boundary is UTF-8, and this unit keeps it that way:
  JSON in and text out are UTF8String from end to end, never converted, never
  routed through `string`. The first version of this unit did convert - it took a
  `string`, called UTF8Encode, and handed the result over - and on Free Pascal in
  its default mode `string` is an AnsiString in the system code page. Every accent
  outside CP1252 was gone before the library ever saw it.

  Nothing failed. The document came out, the smoke test went green, and the only
  sign was that the PDF was 380 bytes larger than the same one produced from C:
  the mangled characters needed more glyphs, so the font subset grew. That is the
  whole warning - a lossy string conversion does not raise anything, it just
  quietly prints the wrong thing.

  So: UTF8String for anything with content in it. Plain `string` only for the two
  things that are names rather than data - a directory and a font family - and
  those are converted explicitly at the call.
  ------------------------------------------------------------------------------
}
unit Frisket;

{$IFDEF FPC}
  {$MODE OBJFPC}{$H+}
  {$MODESWITCH ADVANCEDRECORDS}
{$ENDIF}

interface

uses
  SysUtils, Classes;

type
  EFrisket = class(Exception);

  { Opaque handles. Never look inside one. }
  TErHandle = Pointer;

  TErFormat = (erPdf, erHtml, erSvg);

  { What one document should look like. It travels to the library as JSON, so a
    field added there does not break anything compiled against this unit. }
  TErOptions = record
    Format: TErFormat;
    ArchivablePdfA3b: Boolean;
    Tagged: Boolean;
    SubsetFonts: Boolean;
    InteractiveFields: Boolean;
    BareHtml: Boolean;
    FrenchNumbers: Boolean;

    class function Default: TErOptions; static;
    function ToJson: UTF8String;
  end;

  { The font library. Left empty it draws with the typefaces bundled into the
    library; the first face added makes them step aside. }
  TErFonts = class
  private
    FHandle: TErHandle;
  public
    constructor Create;
    destructor Destroy; override;
    function AddDirectory(const Directory: string): Integer;
    { One font file, named on purpose. Unlike AddDirectory, a file that is not a font
      raises: this was asked about that file and no other. }
    procedure AddFile(const FileName: string);
    { One font from its bytes, for a caller with no file. The bytes are copied. }
    procedure AddMemory(const Data; Length: Int32);
    procedure SetDefaultFamily(const Family: string);
    function ListAsJson: UTF8String;
    property Handle: TErHandle read FHandle;
  end;

  TErTemplate = class
  private
    FHandle: TErHandle;
  public
    constructor CreateFromFile(const Path: string);
    constructor CreateFromJson(const Json: UTF8String);
    destructor Destroy; override;

    { What is wrong with the template, as a JSON array. '[]' means nothing. }
    function CheckAsJson(Accessible: Boolean = True): UTF8String;

    { One document. Data may be empty, in which case the template is rendered
      bare - which is how a template is looked at while it is being drawn. }
    function RenderToBytes(Fonts: TErFonts; const DataJson: UTF8String;
                           const Options: TErOptions): TBytes;
    function RenderToText(Fonts: TErFonts; const DataJson: UTF8String;
                          const Options: TErOptions): UTF8String;
    procedure Render(Fonts: TErFonts; const DataJson: UTF8String;
                     const Options: TErOptions; Stream: TStream);
    procedure RenderToFile(Fonts: TErFonts; const DataJson: UTF8String;
                           const Path: string; const Options: TErOptions);
  end;

function ErVersion: UTF8String;

{ Reads a file as the bytes it holds. Offered here because getting this wrong is
  what corrupts the data before it is ever sent: TStringList would decode the file
  into the system code page and hand back something else. }
function ErReadFileUtf8(const Path: string): UTF8String;

implementation

const
{$IFDEF MSWINDOWS}
  ErLib = 'frisket.dll';
{$ELSE}
  {$IFDEF DARWIN}
  ErLib = 'libfrisket.dylib';
  {$ELSE}
  ErLib = 'libfrisket.so';
  {$ENDIF}
{$ENDIF}

  FK_OK = 0;

{ ---------------------------------------------------------------- the imports }

function fk_last_error: PAnsiChar; cdecl; external ErLib;
function fk_version: PAnsiChar; cdecl; external ErLib;

function fk_fonts_create(out Fonts: TErHandle): Int32; cdecl; external ErLib;
function fk_fonts_add_directory(Fonts: TErHandle; Directory: PAnsiChar;
                                Count: PInt32): Int32; cdecl; external ErLib;
function fk_fonts_add_file(Fonts: TErHandle; FileName: PAnsiChar): Int32; cdecl; external ErLib;
function fk_fonts_add_memory(Fonts: TErHandle; Data: Pointer; Length: Int32): Int32; cdecl; external ErLib;
function fk_fonts_set_default(Fonts: TErHandle; Family: PAnsiChar): Int32; cdecl; external ErLib;
function fk_fonts_list(Fonts: TErHandle; out Buffer: TErHandle): Int32; cdecl; external ErLib;
procedure fk_fonts_free(Fonts: TErHandle); cdecl; external ErLib;

function fk_template_parse(Json: PAnsiChar; out Tmpl: TErHandle): Int32; cdecl; external ErLib;
function fk_template_load(Path: PAnsiChar; out Tmpl: TErHandle): Int32; cdecl; external ErLib;
function fk_template_check(Tmpl: TErHandle; Accessible: Int32;
                           out Buffer: TErHandle): Int32; cdecl; external ErLib;
procedure fk_template_free(Tmpl: TErHandle); cdecl; external ErLib;

function fk_render(Tmpl, Fonts: TErHandle; DataJson, OptionsJson: PAnsiChar; Token: TErHandle;
                   out Buffer: TErHandle): Int32; cdecl; external ErLib;

function fk_buffer_data(Buffer: TErHandle): PByte; cdecl; external ErLib;
function fk_buffer_size(Buffer: TErHandle): Int32; cdecl; external ErLib;
procedure fk_buffer_free(Buffer: TErHandle); cdecl; external ErLib;

{ ------------------------------------------------------------------- plumbing }

{ A pointer to the bytes, or nil for nothing at all. Empty and absent are not the
  same thing on the other side: one is a document with no data, the other is a
  request to render the template bare. }
function Bytes(const Value: UTF8String): PAnsiChar;
begin
  if Length(Value) = 0 then
    Result := nil
  else
    Result := PAnsiChar(Value);
end;

{ A name - a path, a family - encoded for the crossing. These are the only places
  a `string` is converted, and they are names rather than content. }
function NameBytes(const Value: string): UTF8String;
begin
  Result := UTF8Encode(Value);
end;

{ Turns a status into an exception carrying what the library said. Every call goes
  through here: a status quietly ignored is a document silently not produced. }
procedure Check(Status: Int32; const What: string);
var
  Reason: PAnsiChar;
begin
  if Status = FK_OK then
    Exit;

  Reason := fk_last_error;
  if Reason = nil then
    raise EFrisket.Create(What + ' failed');

  { The library's message already names the entry point it came from, so repeating
    What here reads as a stutter: "fk_render: fk_render: not a font library". }
  raise EFrisket.Create(string(UTF8String(Reason)));
end;

{ Copies a buffer out and frees it. Nothing the library owns outlives the call. }
function TakeBytes(Buffer: TErHandle): TBytes;
var
  Size: Int32;
begin
  Result := nil;
  Size := fk_buffer_size(Buffer);
  SetLength(Result, Size);
  if Size > 0 then
    Move(fk_buffer_data(Buffer)^, Result[0], Size);
  fk_buffer_free(Buffer);
end;

{ The same, as UTF-8 text. The bytes are moved, never decoded: they are already
  what they should be, and decoding them is precisely how they get spoiled. }
function TakeText(Buffer: TErHandle): UTF8String;
var
  Size: Int32;
begin
  Result := '';
  Size := fk_buffer_size(Buffer);
  SetLength(Result, Size);
  if Size > 0 then
    Move(fk_buffer_data(Buffer)^, Result[1], Size);
  fk_buffer_free(Buffer);
end;

function ErVersion: UTF8String;
begin
  Result := UTF8String(fk_version);
end;

function ErReadFileUtf8(const Path: string): UTF8String;
var
  Input: TFileStream;
  Size: Int64;
begin
  Result := '';
  Input := TFileStream.Create(Path, fmOpenRead or fmShareDenyWrite);
  try
    Size := Input.Size;
    SetLength(Result, Size);
    if Size > 0 then
      Input.ReadBuffer(Result[1], Size);
  finally
    Input.Free;
  end;
end;

{ -------------------------------------------------------------------- options }

class function TErOptions.Default: TErOptions;
begin
  Result.Format := erPdf;
  Result.ArchivablePdfA3b := False;
  Result.Tagged := False;
  Result.SubsetFonts := True;
  Result.InteractiveFields := True;
  Result.BareHtml := False;
  // Invariant to match the engine's own default; a narrow-space thousands
  // separator is a French convention, not a neutral one.
  Result.FrenchNumbers := False;
end;

function BoolJson(Value: Boolean): UTF8String;
begin
  if Value then Result := 'true' else Result := 'false';
end;

function TErOptions.ToJson: UTF8String;
const
  Formats: array[TErFormat] of UTF8String = ('pdf', 'html', 'svg');
begin
  Result := '{"format":"' + Formats[Format] + '"';
  if ArchivablePdfA3b then
    Result := Result + ',"conformance":"pdfa3b"';
  Result := Result
    + ',"tagged":' + BoolJson(Tagged)
    + ',"subset":' + BoolJson(SubsetFonts)
    + ',"interactive":' + BoolJson(InteractiveFields)
    + ',"bare":' + BoolJson(BareHtml)
    + ',"numbers":"';
  if FrenchNumbers then
    Result := Result + 'french"}'
  else
    Result := Result + 'invariant"}';
end;

{ ---------------------------------------------------------------------- fonts }

constructor TErFonts.Create;
begin
  inherited Create;
  Check(fk_fonts_create(FHandle), 'fk_fonts_create');
end;

destructor TErFonts.Destroy;
begin
  if FHandle <> nil then
  begin
    fk_fonts_free(FHandle);
    FHandle := nil;
  end;
  inherited Destroy;
end;

function TErFonts.AddDirectory(const Directory: string): Integer;
var
  Raw: UTF8String;
  Count: Int32;
begin
  Raw := NameBytes(Directory);
  Count := 0;
  Check(fk_fonts_add_directory(FHandle, Bytes(Raw), @Count), 'fk_fonts_add_directory');
  Result := Count;
end;

procedure TErFonts.AddFile(const FileName: string);
var
  Raw: UTF8String;
begin
  Raw := NameBytes(FileName);
  Check(fk_fonts_add_file(FHandle, Bytes(Raw)), 'fk_fonts_add_file');
end;

procedure TErFonts.AddMemory(const Data; Length: Int32);
begin
  Check(fk_fonts_add_memory(FHandle, @Data, Length), 'fk_fonts_add_memory');
end;

procedure TErFonts.SetDefaultFamily(const Family: string);
var
  Raw: UTF8String;
begin
  Raw := NameBytes(Family);
  Check(fk_fonts_set_default(FHandle, Bytes(Raw)), 'fk_fonts_set_default');
end;

function TErFonts.ListAsJson: UTF8String;
var
  Buffer: TErHandle;
begin
  Check(fk_fonts_list(FHandle, Buffer), 'fk_fonts_list');
  Result := TakeText(Buffer);
end;

{ ------------------------------------------------------------------ templates }

constructor TErTemplate.CreateFromFile(const Path: string);
var
  Raw: UTF8String;
begin
  inherited Create;
  Raw := NameBytes(Path);
  Check(fk_template_load(Bytes(Raw), FHandle), 'fk_template_load');
end;

constructor TErTemplate.CreateFromJson(const Json: UTF8String);
begin
  inherited Create;
  Check(fk_template_parse(Bytes(Json), FHandle), 'fk_template_parse');
end;

destructor TErTemplate.Destroy;
begin
  if FHandle <> nil then
  begin
    fk_template_free(FHandle);
    FHandle := nil;
  end;
  inherited Destroy;
end;

function TErTemplate.CheckAsJson(Accessible: Boolean): UTF8String;
var
  Buffer: TErHandle;
  Flag: Int32;
begin
  if Accessible then Flag := 1 else Flag := 0;
  Check(fk_template_check(FHandle, Flag, Buffer), 'fk_template_check');
  Result := TakeText(Buffer);
end;

function TErTemplate.RenderToBytes(Fonts: TErFonts; const DataJson: UTF8String;
                                   const Options: TErOptions): TBytes;
var
  Buffer: TErHandle;
  Opts: UTF8String;
  FontHandle: TErHandle;
begin
  if Fonts = nil then
    FontHandle := nil
  else
    FontHandle := Fonts.Handle;

  Opts := Options.ToJson;
  Check(fk_render(FHandle, FontHandle, Bytes(DataJson), Bytes(Opts), nil, Buffer), 'fk_render');
  Result := TakeBytes(Buffer);
end;

function TErTemplate.RenderToText(Fonts: TErFonts; const DataJson: UTF8String;
                                  const Options: TErOptions): UTF8String;
var
  Buffer: TErHandle;
  Opts: UTF8String;
  FontHandle: TErHandle;
begin
  if Fonts = nil then
    FontHandle := nil
  else
    FontHandle := Fonts.Handle;

  Opts := Options.ToJson;
  Check(fk_render(FHandle, FontHandle, Bytes(DataJson), Bytes(Opts), nil, Buffer), 'fk_render');
  Result := TakeText(Buffer);
end;

procedure TErTemplate.Render(Fonts: TErFonts; const DataJson: UTF8String;
                             const Options: TErOptions; Stream: TStream);
var
  Produced: TBytes;
begin
  Produced := RenderToBytes(Fonts, DataJson, Options);
  if Length(Produced) > 0 then
    Stream.WriteBuffer(Produced[0], Length(Produced));
end;

procedure TErTemplate.RenderToFile(Fonts: TErFonts; const DataJson: UTF8String;
                                   const Path: string; const Options: TErOptions);
var
  Output: TFileStream;
begin
  Output := TFileStream.Create(Path, fmCreate);
  try
    Render(Fonts, DataJson, Options, Output);
  finally
    Output.Free;
  end;
end;

end.
