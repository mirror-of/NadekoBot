# Creating and inviting your bot

## Creating a Discord application

This document aims to guide you through the process of creating a Discord account for your bot (the Discord Bot application), and inviting that account into your Discord server.

![img2](https://i.imgur.com/9FgPIxd.gif)

- Go to [the Discord developer application page][DiscordApp].
- Log in with your Discord account.
- Create an application.
- On the **General Information** tab, fill out the `Name` field (it's your app's name)
- Upload an image if you want and add an app description. **(Optional)**
- Go to the **Bot** tab on the left sidebar.
- Click on the `Add a Bot` button and confirm that you do want to add a bot to this app.
- Scroll down to the `Privileged Gateway Intents` section and enable both intents.
These are required for a number of features to function properly, and should both be on.

## Inviting your bot to your server

![img4](https://i.imgur.com/9Nr37pI.gif)

- Go to the **0Auth2** tab on the left.
- Check the `bot` option under **scopes**.
- Scroll down and check the `Administrator` option.
- Click the `copy` button.
- Paste link into a new tab.
- Pick your Discord server, click `continue` then `Authorize` and confirm with the captcha at the end.
- The bot should have been added to your server.

[Google Console]: https://console.developers.google.com
[DiscordApp]: https://discordapp.com/developers/applications/me
[Invite Guide]: https://tukimoop.pw/s/guide.html
