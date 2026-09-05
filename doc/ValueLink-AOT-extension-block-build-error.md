# C# 14 extension blockでValueLink/TinyhandのAOTソースジェネレーターが停止する問題

調査日: 2026-09-05

## 結論

ValueLink 0.118.2とTinyhand 0.144.1のAOT用静的登録ジェネレーターは、C# 14のextension blockが存在するコンパイルで停止する。

直接原因は、Roslynがextension declarationを表す特殊な型シンボルに対して
`ITypeSymbol.WithNullableAnnotation(...)`をサポートしていないにもかかわらず、両ジェネレーターがそのシンボルを登録対象として同APIを呼び出すことである。

Netsphereでは次のextension blockがトリガーになる。

```csharp
public static class NetResultExtensions
{
    extension(NetResult result)
    {
        public bool IsSuccess => (result == NetResult.Success) || (result == NetResult.Completed);
        public bool IsFailure => (result != NetResult.Success) && (result != NetResult.Completed);
    }
}
```

この問題はValueLinkとTinyhandの両方にある。ValueLinkだけを修正すると`GoshujinClass`関連のエラーは解消するが、Tinyhandの`THAOT999`が残るため、両パッケージを修正して同時に更新する必要がある。

また、extension型の除外後にTinyhand側で、派生型が基底型の`IStringConvertible<TBase>`を継承した場合に`IStringConvertible<TDerived>`と誤認する別の問題が現れる。この修正も完全なビルド復旧に必要である。

## 確認環境

- .NET SDK 10.0.400
- TargetFramework: `net10.0`
- LangVersion: `Preview`
- ValueLink 0.118.2
- Tinyhand 0.144.1
- Arc.Unit 0.46.1

## 症状

最初に確認すべきエラーは`GoshujinClass`ではなく、ログの先頭にあるジェネレーター例外である。

```text
CSC : warning CS8785: Generator 'ValueLinkGeneratorV2' failed to generate source.
System.NotSupportedException: Specified method is not supported.

CSC : error THAOT999: System.NotSupportedException: Specified method is not supported.
  at Microsoft.CodeAnalysis.CSharp.Symbols.PublicModel.TypeSymbol.
     Microsoft.CodeAnalysis.ITypeSymbol.WithNullableAnnotation(...)
  at Tinyhand.Generator.StaticRegistrationGenerator.Emitter.Emit(...)
```

ValueLinkのジェネレーター全体が停止するため、本来生成される入れ子型がなくなり、多数の二次エラーが発生する。

```text
CS0426: The type name 'GoshujinClass' does not exist in the type '...'
```

未修正パッケージによる`Netsphere/Netsphere.csproj`のRebuildでは、1件の`CS8785`警告と19件のエラーを確認した。そのうち18件の`CS0426`はValueLink停止に伴う二次エラーである。

## 原因

### Roslyn側の動作

C# 14のextension blockは、ソースジェネレーターから`INamedTypeSymbol.IsExtension == true`のシンボルとして観測される。このシンボルはユーザーが静的登録する通常のランタイム型ではない。

Roslynの`ITypeSymbol.WithNullableAnnotation`実装は、基になるnamed typeがextension declarationの場合に`NotSupportedException`を送出する。

参考:

- [C# 14 extension declaration](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extension)
- [Roslyn PublicModel.TypeSymbol implementation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/PublicModel/TypeSymbol.cs)
- [INamedTypeSymbol.IsExtension](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.inamedtypesymbol.isextension)

### ValueLink側の問題

`ValueLinkGenerator/StaticOwnerRegistration.cs`は、コンパイル中の型と呼び出しを広く走査し、閉じた型を静的登録候補に追加する。

現在の`IsClosed`は通常のnamed typeとextension declarationのシンボルを区別しない。そのためextension型が`AddType`へ到達し、次の正規化で例外になる。

```csharp
type = type.WithNullableAnnotation(NullableAnnotation.None);
```

extension declarationは登録可能な型ではないため、Roslyn側の変更を待つのではなく、ValueLink側で除外するのが正しい。

### Tinyhand側の同型の問題

`TinyhandGenerator/StaticRegistrationGenerator.cs`にも同じ走査と`WithNullableAnnotation`呼び出しがある。ValueLinkを修正してもTinyhandが独立して停止する。

### Tinyhand側の派生型誤判定

extension型を除外すると、Netsphereでは次に`ActiveNode`と`LifelineNode`の登録コードがコンパイルエラーになる。

```text
CS0311: 'ActiveNode' cannot be used as type parameter 'T' in
'TinyhandTypeIdentifier.RegisterStringConvertible<T>()'.
There is no implicit reference conversion from 'ActiveNode'
to 'Arc.IStringConvertible<ActiveNode>'.
```

両型は`NetNode`を継承し、`NetNode`は`IStringConvertible<NetNode>`を実装する。`AllInterfaces`にはこの基底型用インターフェースが含まれるが、現在のジェネレーターはインターフェースのmetadata nameしか検査しないため、派生型自身の自己型インターフェースだと誤認する。

## 恒久対策

### 1. ValueLinkでextension型を静的登録候補から除外する

`ValueLinkGenerator/StaticOwnerRegistration.cs`の`IsClosed`に`!named.IsExtension`を追加する。

```diff
 INamedTypeSymbol named =>
     named.TypeKind != TypeKind.Error &&
+    !named.IsExtension &&
     !named.IsAnonymousType &&
     !named.IsUnboundGenericType &&
     (named.ContainingType is null || Check(named.ContainingType, depth - 1)) &&
     named.TypeArguments.All(x => Check(x, depth - 1)),
```

`ITypeSymbol.IsExtension`ではなく`INamedTypeSymbol.IsExtension`を使用する。前者はRoslynでobsoleteになっているバージョンがある。

防御を強める場合は、`AddType`にも次の早期returnを追加できる。

```csharp
if (type is INamedTypeSymbol { IsExtension: true })
{
    return;
}
```

ただし、判定を複数箇所に分散させないという観点では、登録可否を集約している`IsClosed`で除外する修正が最小である。

### 2. Tinyhandでもextension型を除外する

`TinyhandGenerator/StaticRegistrationGenerator.cs`の`IsClosed`に同じ条件を追加する。

```diff
 INamedTypeSymbol named =>
     named.TypeKind != TypeKind.Error &&
+    !named.IsExtension &&
     !named.IsAnonymousType &&
     !named.IsUnboundGenericType &&
     (named.ContainingType is null || IsClosed(named.ContainingType)) &&
     named.TypeArguments.All(IsClosed),
```

### 3. Tinyhandの`IStringConvertible<T>`判定で自己型引数も確認する

metadata nameだけでなく、インターフェースの型引数が登録対象型自身と一致することを確認する。

```diff
-if (named.AllInterfaces.Any(x => MetadataName(x) == "Arc.IStringConvertible`1"))
+if (named.AllInterfaces.Any(x =>
+    MetadataName(x) == "Arc.IStringConvertible`1" &&
+    SymbolEqualityComparer.Default.Equals(x.TypeArguments[0], named)))
 {
     code += $"\nglobal::Tinyhand.TinyhandTypeIdentifier.RegisterStringConvertible<{name}>();";
 }
```

## 回帰テスト

### ValueLink

`LanguageVersion.Preview`で次を同じcompilationに含め、`ValueLinkGeneratorV2`の結果に例外がなく、`Item.GoshujinClass`が生成されることを確認する。

```csharp
using Tinyhand;
using ValueLink;

[TinyhandObject]
[ValueLinkObject]
public partial class Item
{
}

public enum Result
{
    Success,
}

public static class ResultExtensions
{
    extension(Result result)
    {
        public bool IsSuccess => result == Result.Success;
    }
}
```

検証項目:

- `GeneratorRunResult.Exception`がnull
- `CS8785`がない
- `Item.GoshujinClass`が生成される
- 生成後compilationにerror diagnosticがない

### Tinyhand

同じextension blockを`StaticRegistrationGenerator`のテストcompilationに追加し、`THAOT999`が出ないことを確認する。

加えて、自己型インターフェース判定について次の形の回帰テストを追加する。

```csharp
[TinyhandObject]
public partial class Base : Arc.IStringConvertible<Base>
{
    // Interface implementation
}

[TinyhandObject]
public partial class Derived : Base
{
}
```

`Base`用の`RegisterStringConvertible<Base>()`は生成され、`RegisterStringConvertible<Derived>()`は生成されないことを確認する。

## 検証結果

Netsphereにローカルビルドしたジェネレーターを差し替えてRebuildした。

1. 未修正ValueLink + 未修正Tinyhand: ValueLink `CS8785`、Tinyhand `THAOT999`、18件の`GoshujinClass`エラー。
2. 修正ValueLink + 未修正Tinyhand: ValueLink `CS8785`と全`GoshujinClass`エラーが消失。Tinyhand `THAOT999`と、それに伴う静的登録型不在だけが残った。
3. extension除外だけを両方に適用: ジェネレーター例外は消失。Tinyhandの誤った`RegisterStringConvertible<ActiveNode/LifelineNode>()`が2件発生。
4. 3件すべての修正を適用: `Netsphere/Netsphere.csproj`のRebuild成功。0エラー。残った2件は既存コードのStyleCop警告`SA1002`と`SA1120`のみ。

## 修正版パッケージ公開までの回避策

推奨順は次のとおり。

1. 上記修正を取り込んだValueLink/Tinyhandのパッケージを作成し、両方を同時に参照する。
2. C# 14 extension blockを従来形式のextension methodへ一時的に書き換える。Netsphereの`IsSuccess`/`IsFailure`はextension propertyなので、メソッド化する場合は現在の22呼び出し箇所も`()`付きに変更する必要がある。
3. NativeAOT対応が不要なブランチに限り、ValueLink 0.117.2/Tinyhand 0.143.3へ戻す。新しい静的登録機能を利用できないため、AOT対応の恒久策にはならない。

アナライザーを単純に無効化する方法は推奨しない。ValueLinkの通常生成コードとTinyhandのAOT静的登録コードが得られず、別のコンパイルエラーまたはNativeAOT実行時エラーにつながる。

## リリース条件

- ValueLinkとTinyhandの両リポジトリにextension block回帰テストを追加する。
- Tinyhandに自己型`IStringConvertible<T>`の回帰テストを追加する。
- 修正版ValueLinkとTinyhandを同じテストプロジェクトから参照してビルドする。
- `dotnet build Netsphere/Netsphere.csproj -t:Rebuild`が成功する。
- 各リポジトリのNativeAOTテストを`PublishAot=true`で実行する。
