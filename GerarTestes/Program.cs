using GerarDanfse;

Console.WriteLine("Insira um caminho de XML.");
var userInput = Console.ReadLine();
if(userInput == null)
    throw new Exception("Caminho nulo");

var xml = File.ReadAllText(userInput);
var pdf = DanfseGenerator.Gerar(xml, new DanfseDeps());
File.WriteAllBytes("danfse.pdf", pdf);