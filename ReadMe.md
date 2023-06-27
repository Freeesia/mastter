# mastter

## 概要

このアプリケーションは、Mastodonに投稿した内容を自動的にTwitterに再投稿するコマンドラインアプリケーションです。Dockerを利用して常時起動します。

## 必要なもの

1. Docker実行環境
2. Mastodonアカウント
3. Twitterアカウント
4. TwitterとMastodonのAPIキー（個別に申請）

## インストールと実行

### Docker Runを利用した起動

初回起動時にはTwitterのアクセストークンがありません。このため、アプリケーションはURLを発行し、以下のコマンドを用いてDockerコンテナを起動します。    
このとき、コマンドに`-it`オプションを付けてコンテナプロセスと対話します。

```bash
docker run -it \
    -e MastodonUrl="<your-mastodon-url>" \
    -e MastodonToken="<your-mastodon-token>" \
    -e TwitterConsumerKey="<your-twitter-consumer-key>" \
    -e TwitterConsumerSecret="<your-twitter-consumer-secret>" \
    -e TwitterBearerToken="<your-twitter-bearer-token>" \
    ghcr.io/freeesia/mastter:latest
```

起動後、表示されるURLからTwitterにログインし、表示されるPINをアプリケーションに入力します。  
これにより、Twitterのアクセストークンとアクセストークンシークレットが生成されます。

2回目以降の起動時には、初回起動時に生成されたアクセストークンとアクセストークンシークレットを以下のように設定して起動します。  
このときには-dオプションを使用してバックグラウンドで実行します。

```bash
docker run -d \
    -e MastodonUrl="<your-mastodon-url>" \
    -e MastodonToken="<your-mastodon-token>" \
    -e TwitterConsumerKey="<your-twitter-consumer-key>" \
    -e TwitterConsumerSecret="<your-twitter-consumer-secret>" \
    -e TwitterBearerToken="<your-twitter-bearer-token>" \
    -e TwitterAccessToken="<your-twitter-access-token>" \
    -e TwitterAccessTokenSecret="<your-twitter-access-token-secret>" \
    ghcr.io/freeesia/mastter:latest
```

## Docker Composeを利用した起動

2回目以降の起動時にDocker Composeを利用したい場合、以下のようにdocker-compose.ymlファイルを設定します。

```yaml
mastter:
  image: ghcr.io/freeesia/mastter:latest
  environment:
    MastodonUrl: "<your-mastodon-url>"
    MastodonToken: "<your-mastodon-token>"
    TwitterConsumerKey: "<your-twitter-consumer-key>"
    TwitterConsumerSecret: "<your-twitter-consumer-secret>"
    TwitterBearerToken: "<your-twitter-bearer-token>"
    TwitterAccessToken: "<your-twitter-access-token>"
    TwitterAccessTokenSecret: "<your-twitter-access-token-secret>"
```
docker-compose.ymlファイルを設定したら、以下のコマンドでDockerコンテナを起動します。

```bash
docker-compose up -d
```
これでアプリケーションは起動し、あなたのMastodonアカウントに新しい投稿があるたびに、同じ内容をTwitterに再投稿します。