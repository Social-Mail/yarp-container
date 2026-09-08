import http from "node:http";

const s = http.createServer((req, res) => {
    res.statusCode = 404;
    res.end();
});

s.listen(Number(process.env.PORT));

const host = process.env.TEST_HOST;


for(let i=0;i<1000;i++) {
    fetch(`https://${host}`).catch(console.error);
}