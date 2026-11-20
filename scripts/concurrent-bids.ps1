<#
.SYNOPSIS
  Bắn N lượt trả giá ĐỒNG THỜI vào cùng một phiên để kiểm chứng Optimistic Concurrency (xmin).
.EXAMPLE
  ./scripts/concurrent-bids.ps1 -AuctionId <guid> -Count 20
#>
param(
    [Parameter(Mandatory = $true)][string]$AuctionId,
    [int]$Count = 20,
    [string]$ApiBaseUrl = "http://localhost:5100"
)

$auction = Invoke-RestMethod "$ApiBaseUrl/api/auctions/$AuctionId"
$amount = [decimal]$auction.minNextBid
Write-Host "Phiên: $($auction.productName) | Giá hiện tại: $($auction.currentPrice) | Tối thiểu kế tiếp: $amount"
Write-Host "Gửi $Count request đồng thời, tất cả cùng giá $amount (chỉ 1 request được phép thắng)..." -ForegroundColor Cyan

$client = [System.Net.Http.HttpClient]::new()
$tasks = @()
for ($i = 1; $i -le $Count; $i++) {
    $body = @{ bidderName = "Bot-$i"; amount = $amount } | ConvertTo-Json
    $content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, "application/json")
    $tasks += $client.PostAsync("$ApiBaseUrl/api/auctions/$AuctionId/bids", $content)
}
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks)

$results = $tasks | ForEach-Object { [int]$_.Result.StatusCode }
$results | Group-Object | Sort-Object Name | ForEach-Object { Write-Host ("HTTP {0}: {1} request" -f $_.Name, $_.Count) }

$after = Invoke-RestMethod "$ApiBaseUrl/api/auctions/$AuctionId"
Write-Host "Sau khi bắn: giá hiện tại = $($after.currentPrice), dẫn đầu = $($after.highestBidder), tổng lượt = $($after.totalBids)" -ForegroundColor Green
Write-Host "Kỳ vọng: đúng 1 request 201, còn lại 400 (AMOUNT_TOO_LOW sau khi retry & re-validate)."
