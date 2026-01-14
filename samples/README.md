# Samples

## StringVsStringBuilder

Demonstrates performance comparison between `string` concatenation and `StringBuilder`.

### Run the sample

```bash
cd samples/StringVsStringBuilder
dotnet run -c Release
```

### What it tests

| Test | Description |
|------|-------------|
| String concatenation | `s += "a"` in a loop (creates new string each iteration) |
| StringBuilder | `sb.Append('a')` in a loop (reuses buffer) |
| StringBuilder with Capacity | Pre-allocated `StringBuilder(capacity)` |
| String.Join vs StringBuilder | Comparing array joining methods |

### Expected output

StringBuilder should be significantly faster than string concatenation, especially as the number of appends increases:

- **10 appends**: StringBuilder ~2-5x faster
- **100 appends**: StringBuilder ~10-50x faster  
- **1000 appends**: StringBuilder ~100-500x faster

Results are saved to the `results/` folder in Markdown, HTML, and CSV formats.